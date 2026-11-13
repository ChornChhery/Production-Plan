using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TeethSender
{
    /// <summary>
    /// Hears the key button (it types P then Enter) system-wide, even while the
    /// print software is in front. Keys are never blocked: the print software
    /// still receives P + Enter exactly as before.
    ///
    /// The hook lives on its own background thread with its own message loop,
    /// because Windows silently drops a keyboard hook whose thread is busy for
    /// about 300 ms.
    /// </summary>
    internal sealed class PrintKeyDetector : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint WM_QUIT = 0x0012;
        private const uint VK_RETURN = 0x0D;
        private const uint VK_P = 0x50;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly int _windowMs;
        private readonly HookProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private Thread _thread;
        private uint _threadId;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly HashSet<uint> _down = new HashSet<uint>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private bool _prevWasP;
        private long _pMs;

        private readonly Queue<string> _recent = new Queue<string>();
        private readonly object _recentLock = new object();
        private long _prevKeyMs;

        /// <summary>Raised (on the hook thread) once per P + Enter.</summary>
        public event Action PrintKeyPressed;

        public bool IsActive { get { return _hook != IntPtr.Zero; } }

        public PrintKeyDetector(int windowMs)
        {
            _windowMs = windowMs;
            _proc = HookCallback;

            _thread = new Thread(HookThread) { IsBackground = true, Name = "TeethSenderHook" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.WaitOne(3000);
        }

        private void HookThread()
        {
            try
            {
                _threadId = GetCurrentThreadId();
                using (var p = Process.GetCurrentProcess())
                using (var m = p.MainModule)
                {
                    _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(m.ModuleName), 0);
                }
            }
            catch
            {
                _hook = IntPtr.Zero;
            }
            finally
            {
                _ready.Set();
            }

            if (_hook == IntPtr.Zero) return;

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private static bool IsModifier(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 ||
                   (vk >= 0xA0 && vk <= 0xA5);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                    if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _down.Remove(data.vkCode);
                    }
                    else if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && _down.Add(data.vkCode))
                    {
                        OnKeyDown(data.vkCode);
                    }
                }
                catch { }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void OnKeyDown(uint vk)
        {
            long now = _clock.ElapsedMilliseconds;
            Remember(vk, now);

            if (IsModifier(vk)) return;

            if (vk == VK_P)
            {
                _prevWasP = true;
                _pMs = now;
                return;
            }

            if (vk == VK_RETURN)
            {
                bool hit = _prevWasP && (now - _pMs) <= _windowMs;
                _prevWasP = false;
                if (hit)
                {
                    var handler = PrintKeyPressed;
                    if (handler != null) handler();
                }
                return;
            }

            _prevWasP = false;
        }

        private void Remember(uint vk, long now)
        {
            lock (_recentLock)
            {
                string gap = _recent.Count == 0 ? "" : "  +" + (now - _prevKeyMs) + " ms";
                _recent.Enqueue(((Keys)vk).ToString() + gap);
                while (_recent.Count > 12) _recent.Dequeue();
                _prevKeyMs = now;
            }
        }

        /// <summary>The last keys heard, newest last (for the "Show last keys" test).</summary>
        public string[] RecentKeys()
        {
            lock (_recentLock) { return _recent.ToArray(); }
        }

        public void Dispose()
        {
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _threadId = 0;
            }
        }
    }
}
