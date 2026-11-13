using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TeethSender
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "TeethSender_SingleInstance", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Teeth Sender is already running.", "Teeth Sender",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SenderContext());
            }
        }
    }

    /// <summary>
    /// Runs the whole Sender: the key hook (P + Enter), the link to laptop 2,
    /// a small always-visible status bar, and a tray icon with Exit.
    /// </summary>
    internal sealed class SenderContext : ApplicationContext
    {
        private readonly LinkClient _client;
        private readonly PrintKeyDetector _detector;
        private readonly StatusForm _status;
        private readonly NotifyIcon _tray;

        public SenderContext()
        {
            SenderSettings.Load();

            _client = new LinkClient(SenderSettings.Host, SenderSettings.Port);
            _client.Start();

            _detector = new PrintKeyDetector(SenderSettings.KeyWindowMs);
            _detector.PrintKeyPressed += _client.Enqueue;

            _status = new StatusForm(_client, _detector);
            _status.Show();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show last keys (test)", null, (s, e) => ShowLastKeys());
            menu.Items.Add("Send +1 IN (test, adds a real count)", null, (s, e) => _client.Enqueue());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, e) => ExitThread());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Teeth Sender",
                ContextMenuStrip = menu,
                Visible = true
            };
        }

        private void ShowLastKeys()
        {
            string[] keys = _detector.RecentKeys();
            string body = keys.Length == 0
                ? "No keys heard yet. Press the key button once, then open this again."
                : string.Join(Environment.NewLine, keys);

            MessageBox.Show(
                "Hook active: " + (_detector.IsActive ? "yes" : "NO") + Environment.NewLine +
                "Sending to: " + SenderSettings.Host + ":" + SenderSettings.Port + Environment.NewLine + Environment.NewLine +
                "Last keys heard (oldest first):" + Environment.NewLine + body + Environment.NewLine + Environment.NewLine +
                "A good key button press shows P and then Enter within a few hundred ms.",
                "Teeth Sender", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void ExitThreadCore()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _status.Close();
            _detector.Dispose();
            _client.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// Small bar in the bottom-right corner, always on top. It never takes
    /// focus, so it cannot interrupt the print software while someone works.
    /// Green = Teeth on laptop 2 is reachable, red = it is not.
    /// </summary>
    internal sealed class StatusForm : Form
    {
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly LinkClient _client;
        private readonly PrintKeyDetector _detector;
        private readonly Label _label;
        private readonly System.Windows.Forms.Timer _timer;

        public StatusForm(LinkClient client, PrintKeyDetector detector)
        {
            _client = client;
            _detector = detector;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(330, 34);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 10, wa.Bottom - Height - 10);

            _label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 11, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(_label);

            _timer = new System.Windows.Forms.Timer { Interval = 500 };
            _timer.Tick += (s, e) => UpdateText();
            _timer.Start();
            UpdateText();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void UpdateText()
        {
            int waiting = _client.PendingCount;

            if (!_detector.IsActive)
            {
                BackColor = Color.FromArgb(197, 48, 48);
                _label.Text = "Key hook NOT running";
            }
            else if (_client.Connected)
            {
                BackColor = Color.FromArgb(10, 122, 47);
                _label.Text = waiting > 0
                    ? "Teeth link: OK  (sending " + waiting + ")"
                    : "Teeth link: OK  (IN sent since start: " + _client.Delivered + ")";
            }
            else
            {
                BackColor = Color.FromArgb(197, 48, 48);
                _label.Text = "Teeth link: NOT CONNECTED  (waiting: " + waiting + ")";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
