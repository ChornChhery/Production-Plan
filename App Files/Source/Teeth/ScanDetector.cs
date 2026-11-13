using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Teeth
{
    /// <summary>
    /// Hears the barcode scanner on THIS laptop even when another program (the
    /// print software) is in front. A scanner types its code at machine speed
    /// and ends with Enter, so "N or more keys, each within a few milliseconds
    /// of the last, then Enter" = one scan. Normal typing is far too slow to
    /// match. The keys are never blocked: the print software still gets them.
    ///
    /// The hook lives on its own background thread with its own message loop.
    /// That matters: Windows silently drops a keyboard hook whose thread is
    /// busy for ~300 ms, and the dashboard's UI thread can be (writing the
    /// Excel log at a shift change). ScanDetected is raised on that hook thread,
    /// so subscribers must return immediately (use BeginInvoke).
    /// </summary>
    internal sealed class ScanDetector : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint VK_RETURN = 0x0D;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
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

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_QUIT = 0x0012;

        private readonly int _minChars;
        private readonly int _maxGapMs;
        private readonly HookProc _proc;      // kept in a field so the GC cannot free it
        private IntPtr _hook = IntPtr.Zero;
        private Thread _thread;
        private uint _threadId;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly HashSet<uint> _down = new HashSet<uint>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private int _burst;
        private long _lastKeyMs = -100000;

        public event Action ScanDetected;

        /// <summary>false if Windows refused the hook (then OUT can only be
        /// counted with the Ctrl+Alt+O fallback).</summary>
        public bool IsActive { get { return _hook != IntPtr.Zero; } }

        public ScanDetector(int minChars, int maxGapMs)
        {
            _minChars = minChars;
            _maxGapMs = maxGapMs;
            _proc = HookCallback;

            _thread = new Thread(HookThread) { IsBackground = true, Name = "TeethScanHook" };
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

            // Message loop: a low-level hook only gets called while its thread pumps messages.
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
            // Shift, Ctrl, Alt, CapsLock and their left/right variants: scanners
            // send Shift for capital letters, which must not break a burst.
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
                    else if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        // Already down = keyboard auto-repeat: ignore.
                        if (_down.Add(data.vkCode))
                        {
                            OnKeyDown(data.vkCode);
                        }
                    }
                }
                catch
                {
                    // Never let an exception escape a hook callback.
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void OnKeyDown(uint vk)
        {
            if (IsModifier(vk)) return;

            long now = _clock.ElapsedMilliseconds;
            long gap = now - _lastKeyMs;

            if (vk == VK_RETURN)
            {
                bool isScan = _burst >= _minChars && gap <= _maxGapMs;
                _burst = 0;
                _lastKeyMs = now;

                if (isScan)
                {
                    var handler = ScanDetected;
                    if (handler != null) handler();
                }
                return;
            }

            if (_burst > 0 && gap > _maxGapMs)
            {
                _burst = 0;
            }
            _burst++;
            _lastKeyMs = now;
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
