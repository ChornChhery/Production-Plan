using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using Teeth;
using TeethSender;

namespace Teeth { internal static class AppBootstrap { public static string AppFilesDir { get { return Path.GetTempPath(); } } } }

// A fake "cable": forwards TCP between sender and Teeth. Mode: 0 = cable OK, 1 = cable pulled, 2 = replies lost.
class Cable
{
    public volatile int Mode;
    readonly int _listen, _target; TcpListener _l; volatile bool _stop;
    readonly List<TcpClient> _open = new List<TcpClient>();
    public Cable(int listen, int target) { _listen = listen; _target = target; }
    public void Start()
    {
        _l = new TcpListener(IPAddress.Loopback, _listen); _l.Start();
        new Thread(() => { while (!_stop) { TcpClient c; try { c = _l.AcceptTcpClient(); } catch { return; }
            if (Mode == 1) { try { c.Close(); } catch { } continue; }
            new Thread(() => Pipe(c)) { IsBackground = true }.Start(); } }) { IsBackground = true }.Start();
        new Thread(() => { while (!_stop) { Thread.Sleep(50); if (Mode == 1) KillAll(); } }) { IsBackground = true }.Start();
    }
    void KillAll() { lock (_open) { foreach (var c in _open) { try { c.Close(); } catch { } } _open.Clear(); } }
    void Pipe(TcpClient a)
    {
        TcpClient b = null;
        try { b = new TcpClient(); b.Connect(IPAddress.Loopback, _target); } catch { try { a.Close(); } catch { } return; }
        lock (_open) { _open.Add(a); _open.Add(b); }
        Thread up = new Thread(() => Copy(a, b, false)) { IsBackground = true }; up.Start();
        Thread down = new Thread(() => Copy(b, a, true)) { IsBackground = true }; down.Start();
    }
    void Copy(TcpClient from, TcpClient to, bool isReply)
    {
        byte[] buf = new byte[1024];
        try
        {
            var fs = from.GetStream(); var ts = to.GetStream();
            while (true)
            {
                int n = fs.Read(buf, 0, buf.Length); if (n <= 0) break;
                if (Mode == 1) break;
                if (isReply && Mode == 2) { try { from.Close(); to.Close(); } catch { } break; }   // Teeth counted it, but the OK never arrives
                ts.Write(buf, 0, n);
            }
        }
        catch { }
        try { from.Close(); } catch { } try { to.Close(); } catch { }
    }
}

static class Sim
{
    static int pass, fail;
    static void Check(string what, bool ok, string detail = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (detail.Length > 0 ? "   [" + detail + "]" : "")); }
    static bool WaitFor(Func<bool> f, int ms) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (f()) return true; Thread.Sleep(50); } return f(); }
    static int PendingLines() { try { return File.Exists("pending.txt") ? File.ReadAllLines("pending.txt").Length : 0; } catch { return -1; } }

    static void Main()
    {
        if (File.Exists("pending.txt")) File.Delete("pending.txt");
        int teethCount = 0;
        LinkSettings.LinkTimeoutSeconds = 6;
        var server = new LinkServer(5077); server.InReceived += () => Interlocked.Increment(ref teethCount); server.Start();
        var cable = new Cable(5078, 5077); cable.Start();
        SenderSettings.Host = "127.0.0.1"; SenderSettings.Port = 5078;
        var client = new LinkClient(SenderSettings.Host, SenderSettings.Port); client.Start();

        Console.WriteLine("TEST 1  normal: 10 key presses");
        for (int i = 0; i < 10; i++) client.Enqueue();
        Check("all 10 delivered, Teeth IN = 10", WaitFor(() => teethCount == 10 && client.PendingCount == 0, 8000), "IN=" + teethCount + " waiting=" + client.PendingCount);
        Check("link shows connected (green)", WaitFor(() => server.IsConnected && client.Connected, 6000));

        Console.WriteLine("TEST 2  cable PULLED for 12 s, 25 key presses meanwhile, then plugged back");
        cable.Mode = 1;
        Check("Teeth link goes red (NOT connected) within 10 s", WaitFor(() => !server.IsConnected, 10000));
        Check("sender notices the cable is out", WaitFor(() => !client.Connected, 6000));
        for (int i = 0; i < 25; i++) client.Enqueue();
        Thread.Sleep(1000);
        Check("25 counts wait in the sender, Teeth still 10", client.PendingCount == 25 && teethCount == 10, "waiting=" + client.PendingCount + " IN=" + teethCount);
        Check("they are saved in pending.txt", WaitFor(() => PendingLines() == 25, 3000), "lines=" + PendingLines());
        Thread.Sleep(10000);
        var sw = Stopwatch.StartNew(); cable.Mode = 0;
        bool back = WaitFor(() => server.IsConnected && client.Connected && client.PendingCount == 0 && teethCount == 35, 20000);
        Check("after plugging back: green again and IN = 35 (each once)", back, "IN=" + teethCount + " waiting=" + client.PendingCount + " time=" + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");
        Console.WriteLine("        recovery time after plugging in: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");

        Console.WriteLine("TEST 3  OK reply lost (Teeth counted, sender never heard OK): no double count");
        cable.Mode = 2;
        for (int i = 0; i < 5; i++) client.Enqueue();
        Thread.Sleep(7000);
        cable.Mode = 0;
        Check("after the link is back: IN = 40 exactly (not 45 or more)", WaitFor(() => client.PendingCount == 0 && teethCount >= 40, 20000) && teethCount == 40, "IN=" + teethCount + " waiting=" + client.PendingCount);

        Console.WriteLine("TEST 4  laptop 1 restarted while 3 counts were waiting (cable out)");
        cable.Mode = 1; Thread.Sleep(1500);
        for (int i = 0; i < 3; i++) client.Enqueue();
        Thread.Sleep(600);
        client.Dispose();
        Check("pending.txt kept the 3 counts on disk", PendingLines() == 3, "lines=" + PendingLines());
        var client2 = new LinkClient(SenderSettings.Host, SenderSettings.Port); client2.Start();
        Check("new sender process loads the 3 waiting counts", client2.PendingCount == 3, "waiting=" + client2.PendingCount);
        cable.Mode = 0;
        Check("they arrive after the cable is back: IN = 43", WaitFor(() => client2.PendingCount == 0 && teethCount == 43, 20000), "IN=" + teethCount);

        Console.WriteLine("TEST 5  Teeth closed on laptop 2 while key presses happen, then opened again");
        cable.Mode = 1; server.Dispose(); Thread.Sleep(800);   // closing Teeth.exe ends the process, so its connections die too
        for (int i = 0; i < 4; i++) client2.Enqueue();
        Thread.Sleep(2500);
        Check("4 counts wait while Teeth is closed", client2.PendingCount == 4, "waiting=" + client2.PendingCount);
        var server2 = new LinkServer(5077); int c2 = 0; server2.InReceived += () => Interlocked.Increment(ref c2); server2.Start();
        cable.Mode = 0;
        Check("Teeth opened again: the 4 counts arrive", WaitFor(() => client2.PendingCount == 0 && c2 == 4, 20000), "IN(new)=" + c2);

        Console.WriteLine();
        Console.WriteLine("RESULT: " + pass + " passed, " + fail + " failed");
        client2.Dispose(); Environment.Exit(fail == 0 ? 0 : 1);
    }
}
