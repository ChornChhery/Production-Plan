using System;
using System.Drawing;
using System.Windows.Forms;

namespace Teeth
{
    /// <summary>
    /// Phase 5: the hidden gate in front of ScheduleSettingsForm. There is no
    /// visible button for this anywhere on MainDashboard - it only opens via
    /// a keyboard shortcut (Ctrl+Alt+S), matching the "hidden Admin PIN
    /// correction window" called out in the phase plan.
    ///
    /// Restyled to match the reference Tkinter "Admin Access" popup: centered
    /// bold navy title, a big centered masked PIN box, and a blue
    /// "Verify PIN" button - same visual language as MainDashboard and
    /// ScheduleSettingsForm.
    ///
    /// NOTE: the PIN below is a hardcoded placeholder ("mgss"). There's no
    /// settings table yet for an admin-changeable PIN - if you want the
    /// shop floor to be able to change it themselves later, that's a small
    /// follow-up (one more column on ShiftConfig, or its own AdminSettings
    /// table).
    /// </summary>
    public class AdminPinForm : Form
    {
        private const string AdminPin = "mgss";

        private static readonly Color ColorNavy = ColorTranslator.FromHtml("#1a365d");
        private static readonly Color ColorBg = ColorTranslator.FromHtml("#f7fafc");
        private static readonly Color ColorBlue = ColorTranslator.FromHtml("#2b6cb0");
        private static readonly Color ColorErrorRed = ColorTranslator.FromHtml("#c53030");

        private readonly TextBox _txtPin;
        private readonly Label _lblError;

        private AdminPinForm()
        {
            Text = "Admin Access";
            Icon = AppBootstrap.AppIcon;
            ClientSize = new Size(340, 220);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = ColorBg;

            var lblTitle = new Label
            {
                Text = "Admin Required",
                AutoSize = true,
                Font = new Font("Arial", 14, FontStyle.Bold),
                ForeColor = ColorNavy,
                BackColor = ColorBg
            };
            Controls.Add(lblTitle);

            _txtPin = new TextBox
            {
                Width = 120,
                Height = 34,
                PasswordChar = '*',
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Arial", 20, FontStyle.Bold),
                BackColor = Color.White
            };
            Controls.Add(_txtPin);

            _lblError = new Label
            {
                Text = "",
                AutoSize = true,
                Font = new Font("Arial", 9, FontStyle.Bold),
                ForeColor = ColorErrorRed,
                BackColor = ColorBg
            };
            Controls.Add(_lblError);

            var btnOk = new Button
            {
                Text = "Verify PIN",
                Size = new Size(150, 38),
                BackColor = ColorBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += BtnOk_Click;
            Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(150, 30),
                BackColor = ColorBg,
                ForeColor = Color.Gray,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 9)
            };
            btnCancel.FlatAppearance.BorderColor = Color.LightGray;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            // Center everything horizontally, same "centered dialog" feel as
            // the reference popup, rather than the old left-aligned form.
            CenterControlsHorizontally(lblTitle, 20);
            CenterControlsHorizontally(_txtPin, 62);
            CenterControlsHorizontally(_lblError, 102);
            CenterControlsHorizontally(btnOk, 124);
            CenterControlsHorizontally(btnCancel, 168);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Shown += (s, e) => _txtPin.Focus();
        }

        private void CenterControlsHorizontally(Control c, int y)
        {
            c.Location = new Point((ClientSize.Width - c.Width) / 2, y);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (_txtPin.Text == AdminPin)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _lblError.Text = "Incorrect PIN.";
            CenterControlsHorizontally(_lblError, 102);
            _txtPin.Clear();
            _txtPin.Focus();
        }

        /// <summary>
        /// Shows the PIN prompt modally; returns true only if the correct
        /// PIN was entered before the dialog closed. Caller is responsible
        /// for what happens next (i.e. opening ScheduleSettingsForm).
        /// </summary>
        public static bool Prompt(IWin32Window owner)
        {
            using (var form = new AdminPinForm())
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
