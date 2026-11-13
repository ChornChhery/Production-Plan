using System;
using System.Drawing;
using System.Windows.Forms;

namespace Teeth
{
    /// <summary>
    /// Reachable via the "Admin" button inside ScheduleSettingsForm (itself
    /// gated by AdminPinForm - so this is a second PIN check, matching the
    /// reference's nested "Admin" -> PIN -> sensor correction flow). Lets
    /// the admin manually overwrite the live IN/OUT counters and the
    /// frozen-time value directly against the LiveCounts table, for when a
    /// physical sensor miscounts and the board needs a manual correction.
    ///
    /// This was flagged in earlier rounds as functionality the C# app was
    /// missing entirely versus the reference. It's implemented now against
    /// DatabaseManager.LoadLiveCounts()/SaveLiveCounts(), which already
    /// existed - MainDashboard's own 3-second autosave loop will simply
    /// pick up whatever this form commits on its next tick.
    /// </summary>
    public class SensorCorrectionForm : Form
    {
        private static readonly Color ColorBg = ColorTranslator.FromHtml("#f7fafc");
        private static readonly Color ColorAmber = ColorTranslator.FromHtml("#b7791f");
        private static readonly Color ColorText = ColorTranslator.FromHtml("#2d3748");

        private readonly DatabaseManager _db;
        private TextBox _txtIn;
        private TextBox _txtOut;
        private TextBox _txtFrozen;
        private Label _lblError;

        public SensorCorrectionForm(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            InitializeComponent();
            Load += (s, e) => LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            Text = "Sensor Correction";
            Icon = AppBootstrap.AppIcon;
            ClientSize = new Size(420, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = ColorBg;

            var lblTitle = new Label
            {
                Text = "Sensor Correction Result",
                AutoSize = true,
                Font = new Font("Arial", 13, FontStyle.Bold),
                ForeColor = ColorAmber,
                BackColor = ColorBg
            };
            lblTitle.Location = new Point((ClientSize.Width - lblTitle.PreferredWidth) / 2, 18);
            Controls.Add(lblTitle);

            _txtIn = AddField("Input count (IN)", 70);
            _txtOut = AddField("Output count (OUT)", 116);
            _txtFrozen = AddField("Frozen time (sec)", 162);

            _lblError = new Label
            {
                AutoSize = false,
                Width = 380,
                Height = 20,
                Location = new Point(20, 208),
                ForeColor = ColorTranslator.FromHtml("#c53030"),
                Font = new Font("Arial", 8.5f, FontStyle.Bold),
                BackColor = ColorBg,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_lblError);

            var btnCommit = new Button
            {
                Text = "Commit changes",
                Size = new Size(200, 40),
                BackColor = ColorAmber,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            btnCommit.FlatAppearance.BorderSize = 0;
            btnCommit.Location = new Point((ClientSize.Width - btnCommit.Width) / 2, 238);
            btnCommit.Click += BtnCommit_Click;
            Controls.Add(btnCommit);

            var btnClose = new Button
            {
                Text = "Close",
                Size = new Size(120, 30),
                BackColor = ColorBg,
                ForeColor = Color.Gray,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 9)
            };
            btnClose.FlatAppearance.BorderColor = Color.LightGray;
            btnClose.Location = new Point((ClientSize.Width - btnClose.Width) / 2, 282);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        private TextBox AddField(string label, int y)
        {
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Location = new Point(30, y + 4),
                Font = new Font("Arial", 10, FontStyle.Bold),
                ForeColor = ColorText,
                BackColor = ColorBg
            };
            Controls.Add(lbl);

            var txt = new TextBox
            {
                Width = 90,
                Location = new Point(300, y),
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Arial", 11, FontStyle.Bold)
            };
            Controls.Add(txt);
            return txt;
        }

        private void LoadCurrentValues()
        {
            var counts = _db.LoadLiveCounts();
            _txtIn.Text = counts.CountIn.ToString();
            _txtOut.Text = counts.CountOut.ToString();
            _txtFrozen.Text = counts.FrozenSeconds.ToString("0.#");
        }

        private void BtnCommit_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(_txtIn.Text.Trim(), out int newIn) || newIn < 0)
            {
                _lblError.Text = "IN must be a whole number, 0 or more.";
                return;
            }
            if (!int.TryParse(_txtOut.Text.Trim(), out int newOut) || newOut < 0)
            {
                _lblError.Text = "OUT must be a whole number, 0 or more.";
                return;
            }
            if (!double.TryParse(_txtFrozen.Text.Trim(), out double newFrozen) || newFrozen < 0)
            {
                _lblError.Text = "Frozen time must be a number, 0 or more.";
                return;
            }

            _lblError.Text = "";

            _db.SaveLiveCounts(new LiveCountsModel
            {
                CountIn = newIn,
                CountOut = newOut,
                FrozenSeconds = newFrozen
            });

            MessageBox.Show(this, "Counts updated.", "Sensor Correction",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
