using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Teeth
{
    /// <summary>
    /// Listens on the cable between the two laptops. Laptop 1's Sender sends
    /// plain text lines:
    ///     PING          -> answered with  PONG   (keeps the status light green)
    ///     IN &lt;id&gt;      -> answered with  OK &lt;id&gt;  (and InReceived fires once)
    /// The id makes a retry harmless: if the Sender never saw our OK and sends
    /// the same id again, we answer OK again but do NOT count it twice.
    /// Everything here runs on background threads; InReceived must be
    /// marshalled to the UI thread by the subscriber.
    /// </summary>
    internal sealed class LinkServer : IDisposable
    {
        private readonly int _port;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _stopping;

        private long _lastSeenTicks;

        private readonly HashSet<string> _seenIds = new HashSet<string>();
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private readonly object _seenLock = new object();
        private const int MaxRememberedIds = 5000;

        /// <summary>Raised (on a background thread) once for every NEW "+1 IN".</summary>
        public event Action InReceived;

        /// <summary>null when listening fine; otherwise why it could not start
        /// (e.g. the port is already in use).</summary>
        public string StartError { get; private set; }

        public LinkServer(int port)
        {
            _port = port;
        }

        public bool IsConnected
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastSeenTicks);
                if (ticks == 0) return false;
                double secs = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
                return secs <= LinkSettings.LinkTimeoutSeconds;
            }
        }

        public void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();

                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "TeethLinkAccept" };
                _acceptThread.Start();
            }
            catch (Exception ex)
            {
                StartError = ex.Message;
            }
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (_stopping) return;
                    Thread.Sleep(200);
                    continue;
                }

                var t = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "TeethLinkClient" };
                t.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 5000;

                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

                    string line;
                    while (!_stopping && (line = reader.ReadLine()) != null)
                    {
                        Touch();
                        line = line.Trim();

                        if (line == "PING")
                        {
                            writer.WriteLine("PONG");
                        }
                        else if (line.StartsWith("IN ", StringComparison.Ordinal))
                        {
                            string id = line.Substring(3).Trim();
                            if (id.Length == 0 || id.Length > 64)
                            {
                                writer.WriteLine("ERR");
                                continue;
                            }

                            if (RememberId(id))
                            {
                                var handler = InReceived;
                                if (handler != null) handler();
                            }
                            writer.WriteLine("OK " + id);
                        }
                        else
                        {
                            writer.WriteLine("ERR");
                        }
                    }
                }
            }
            catch
            {
                // Cable pulled / sender closed: just end this connection.
            }
        }

        private void Touch()
        {
            Interlocked.Exchange(ref _lastSeenTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>true = first time we see this id (count it).</summary>
        private bool RememberId(string id)
        {
            lock (_seenLock)
            {
                if (!_seenIds.Add(id)) return false;

                _seenOrder.Enqueue(id);
                while (_seenOrder.Count > MaxRememberedIds)
                {
                    _seenIds.Remove(_seenOrder.Dequeue());
                }
                return true;
            }
        }

        public void Dispose()
        {
            _stopping = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }
    }
}
