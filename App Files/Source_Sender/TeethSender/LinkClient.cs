using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TeethSender
{
    /// <summary>
    /// Sends "+1 IN" to Teeth on laptop 2 over the cable and makes sure none
    /// is lost:
    ///   * every count gets a unique id and waits in a queue until Teeth
    ///     answers "OK &lt;id&gt;";
    ///   * the queue is saved to pending.txt, so even a reboot of this laptop
    ///     (or Teeth being off) loses nothing;
    ///   * a "PING" every 2 s keeps the green light on in Teeth and lets this
    ///     program notice quickly when the cable is pulled;
    ///   * if the connection drops it reconnects by itself.
    /// A retry of an id that Teeth already counted is answered "OK" without
    /// being counted twice (Teeth remembers recent ids).
    /// </summary>
    internal sealed class LinkClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _pendingPath;

        private readonly List<string> _pending = new List<string>();
        private readonly object _lock = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private volatile bool _stop;
        private volatile bool _connected;
        private volatile bool _dirty;
        private long _delivered;
        private Thread _thread;

        public LinkClient(string host, int port)
        {
            _host = host;
            _port = port;
            _pendingPath = Path.Combine(SenderSettings.BaseDir, "pending.txt");
            LoadPending();
        }

        public bool Connected { get { return _connected; } }

        public int PendingCount
        {
            get { lock (_lock) { return _pending.Count; } }
        }

        /// <summary>Counts confirmed by Teeth since this program started.</summary>
        public long Delivered { get { return Interlocked.Read(ref _delivered); } }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "TeethSenderLink" };
            _thread.Start();
        }

        /// <summary>Queue one "+1 IN". Returns immediately.</summary>
        public void Enqueue()
        {
            string id = Guid.NewGuid().ToString("N");
            lock (_lock) { _pending.Add(id); }
            _dirty = true;
            _wake.Set();
        }

        private void Run()
        {
            while (!_stop)
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    IAsyncResult ar = client.BeginConnect(_host, _port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(2000))
                    {
                        throw new TimeoutException("connect");
                    }
                    client.EndConnect(ar);

                    client.NoDelay = true;
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;

                    NetworkStream stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

                    _connected = true;
                    DateTime lastPing = DateTime.MinValue;

                    while (!_stop)
                    {
                        SavePendingIfDirty();

                        string id = NextPending();
                        if (id != null)
                        {
                            writer.WriteLine("IN " + id);
                            string reply = reader.ReadLine();
                            if (reply == null || reply != "OK " + id)
                            {
                                throw new IOException("unexpected reply");
                            }

                            RemovePending(id);
                            Interlocked.Increment(ref _delivered);
                            continue;
                        }

                        if ((DateTime.UtcNow - lastPing).TotalSeconds >= 2)
                        {
                            writer.WriteLine("PING");
                            if (reader.ReadLine() != "PONG")
                            {
                                throw new IOException("no pong");
                            }
                            lastPing = DateTime.UtcNow;
                        }

                        _wake.WaitOne(200);
                    }
                }
                catch
                {
                    // Cable out, Teeth closed, timeout...: fall through and retry.
                }
                finally
                {
                    _connected = false;
                    if (client != null)
                    {
                        try { client.Close(); } catch { }
                    }
                }

                SavePendingIfDirty();
                if (!_stop) _wake.WaitOne(1500);
            }
        }

        private string NextPending()
        {
            lock (_lock) { return _pending.Count > 0 ? _pending[0] : null; }
        }

        private void RemovePending(string id)
        {
            lock (_lock)
            {
                int i = _pending.IndexOf(id);
                if (i >= 0) _pending.RemoveAt(i);
            }
            _dirty = true;
        }

        private void LoadPending()
        {
            try
            {
                if (!File.Exists(_pendingPath)) return;
                foreach (string line in File.ReadAllLines(_pendingPath))
                {
                    string id = line.Trim();
                    if (id.Length > 0 && id.Length <= 64) _pending.Add(id);
                }
            }
            catch { }
        }

        private void SavePendingIfDirty()
        {
            if (!_dirty) return;
            _dirty = false;

            string[] copy;
            lock (_lock) { copy = _pending.ToArray(); }

            try { File.WriteAllLines(_pendingPath, copy); }
            catch { _dirty = true; }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            try { if (_thread != null) _thread.Join(1500); } catch { }
            SavePendingIfDirty();
        }
    }
}
