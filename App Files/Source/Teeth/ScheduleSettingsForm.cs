using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Teeth
{
    /// <summary>
    /// Phase 5: the Settings window, reachable only through the hidden Admin
    /// PIN gate (AdminPinForm, Ctrl+Alt+S on MainDashboard). Lets the admin
    /// pick the active Line and edit the Day/Night WorkBlocks timelines
    /// that ShiftCalculator/TargetCalculator (Phase 4) read from.
    ///
    /// Third round of feedback baked into this version:
    ///  - Start/End are back to TWO locked dropdowns each (Hour, then
    ///    Minute) visually grouped under one "START TIME"/"END TIME"
    ///    header with a ":" spacer column between them, matching the real
    ///    reference screenshot exactly instead of the single-dropdown
    ///    simplification from the previous round.
    ///  - A gap between one block's End and the next block's Start is a
    ///    real break, not an error - there's no separate break row/flag
    ///    anywhere. TargetCalculator (ShiftLogic.cs) was fixed to position
    ///    every block from its own absolute Start/End instead of chaining
    ///    by summed duration, which is what actually made gaps behave as
    ///    idle time. Validation here now only flags a genuine OVERLAP
    ///    (next block starting before the previous one ends), not a gap.
    ///  - Rows with no Start/End chosen yet render in light gray, matching
    ///    the reference's "Work 9" placeholder-row treatment.
    ///  - The "Admin" button is back, opening a second PIN check
    ///    (AdminPinForm) and then the new SensorCorrectionForm - the
    ///    sensor-count-override feature previous rounds only flagged as
    ///    missing is now actually implemented.
    ///  - Default seed data (DatabaseManager) now runs a full 10-block
    ///    timeline from shift start to shift end instead of stopping at
    ///    Work 8 partway through the shift.
    /// </summary>
    public partial class ScheduleSettingsForm : Form
    {
        private const int DefaultRowCount = 10; // only pads a totally fresh, never-configured DB

        private static readonly string[] LineChoices = { "Line A", "Line B", "Line C", "Line D" };
        private static readonly string[] DayHours = Enumerable.Range(8, 13).Select(h => h.ToString("00")).ToArray();
        private static readonly string[] NightHours = Enumerable.Range(20, 4).Select(h => h.ToString("00"))
            .Concat(Enumerable.Range(0, 9).Select(h => h.ToString("00"))).ToArray();
        private static readonly string[] MinuteSteps = new[] { "" }.Concat(Enumerable.Range(0, 12).Select(i => (i * 5).ToString("00"))).ToArray();

        private static readonly Color ColorBg = ColorTranslator.FromHtml("#f7fafc");
        private static readonly Color ColorNavy = ColorTranslator.FromHtml("#1a365d");
        private static readonly Color ColorGray = ColorTranslator.FromHtml("#4a5568");
        private static readonly Color ColorBlue = ColorTranslator.FromHtml("#2b6cb0");
        private static readonly Color ColorRed = ColorTranslator.FromHtml("#c53030");
        private static readonly Color ColorRedBg = ColorTranslator.FromHtml("#fff5f5");
        private static readonly Color ColorGreen = ColorTranslator.FromHtml("#276749");
        private static readonly Color ColorGreenBg = ColorTranslator.FromHtml("#f0fff4");
        private static readonly Color ColorPlaceholderText = ColorTranslator.FromHtml("#a0aec0");

        private static readonly Color ColorGreenBoxBg = ColorTranslator.FromHtml("#f0fff4");
        private static readonly Color ColorGreenBoxFg = ColorTranslator.FromHtml("#276749");
        private static readonly Color ColorBlueBoxBg = ColorTranslator.FromHtml("#ebf8ff");
        private static readonly Color ColorBlueBoxFg = ColorTranslator.FromHtml("#2b6cb0");

        private static readonly Color ColorTargetInBg = ColorTranslator.FromHtml("#c6f6d5");
        private static readonly Color ColorTargetInFg = ColorTranslator.FromHtml("#22543d");
        private static readonly Color ColorTargetOutBg = ColorTranslator.FromHtml("#bee3f8");
        private static readonly Color ColorTargetOutFg = ColorTranslator.FromHtml("#2a4365");
        private static readonly Color ColorTactBg = ColorTranslator.FromHtml("#edf2f7");
        private static readonly Color ColorRowAlt = ColorTranslator.FromHtml("#f7fafc");

        private const int FormWidth = 1040;

        private readonly DatabaseManager _db;
        private readonly ShiftConfigModel _config;

        private List<WorkBlockModel> _dayBlocks;
        private List<WorkBlockModel> _nightBlocks;
        private string _currentProfile = "Day Schedule";

        private ComboBox _cmbLine;
        private Button _btnDay;
        private Button _btnNight;
        private TextBox _txtPlanIn;
        private TextBox _txtPlanOut;
        private DataGridView _grid;
        private Label _lblErrors;
        private Button _btnSave;
        private Button _btnCancel;
        private Button _btnAdmin;
        private Button _btnAddBlock;
        private Button _btnRemoveBlock;
        private Button _btnCloseBlock;

        private int _colName, _colStartHour, _colStartMin,
                    _colEndHour, _colEndMin, _colTargetIn, _colTargetOut, _colTact;
        private bool _suspendRecalc;

        public ScheduleSettingsForm(DatabaseManager db, ShiftConfigModel config)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            InitializeComponent();
            Load += ScheduleSettingsForm_Load;
        }

        // ------------------------------------------------------------------
        //  FORM SHELL
        // ------------------------------------------------------------------
        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Schedule Managements";
            Icon = AppBootstrap.AppIcon;
            ClientSize = new Size(FormWidth, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = ColorBg;

            Controls.Add(BuildTopPanel());
            Controls.Add(BuildBottomPanel());

            var gridHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBg,
                Padding = new Padding(20, 0, 20, 8)
            };

            // IMPORTANT:
            // Add the Fill control first and the Top toolbar second.
            // WinForms docking uses the control order when calculating
            // the available layout area. Adding the DataGridView after
            // the toolbar can cause the Fill grid to cover the toolbar
            // and the column-header area.
            _grid = BuildGrid();
            gridHost.Controls.Add(_grid);

            var toolbar = BuildToolbar();
            gridHost.Controls.Add(toolbar);

            Controls.Add(gridHost);
            gridHost.BringToFront();

            ResumeLayout(false);
        }

        private Panel BuildTopPanel()
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = ColorBg };

            var lblLine = new Label { Text = "PRODUCTION LINE", AutoSize = true, Location = new Point(20, 8), Font = new Font("Arial", 8, FontStyle.Bold), ForeColor = ColorGray, BackColor = ColorBg };
            topPanel.Controls.Add(lblLine);

            _cmbLine = new ComboBox
            {
                Location = new Point(20, 28),
                Width = 200,
                Height = 30,
                Font = new Font("Arial", 14, FontStyle.Bold),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbLine.Items.AddRange(LineChoices);
            topPanel.Controls.Add(_cmbLine);

            var lblShift = new Label { Text = "SHIFT WORK", AutoSize = true, Location = new Point(240, 8), Font = new Font("Arial", 8, FontStyle.Bold), ForeColor = ColorGray, BackColor = ColorBg };
            topPanel.Controls.Add(lblShift);

            _btnDay = new Button { Text = "Day   08:00 - 20:00", Location = new Point(240, 26), Size = new Size(170, 36), FlatStyle = FlatStyle.Flat, Font = new Font("Arial", 10, FontStyle.Bold) };
            _btnDay.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#cbd5e0");
            _btnDay.Click += (s, e) => SetProfile("Day Schedule");
            topPanel.Controls.Add(_btnDay);

            _btnNight = new Button { Text = "Night   20:00 - 08:00", Location = new Point(410, 26), Size = new Size(170, 36), FlatStyle = FlatStyle.Flat, Font = new Font("Arial", 10, FontStyle.Bold) };
            _btnNight.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#cbd5e0");
            _btnNight.Click += (s, e) => SetProfile("Night Schedule");
            topPanel.Controls.Add(_btnNight);

            var planInPanel = new Panel { Location = new Point(640, 8), Size = new Size(180, 62), BackColor = ColorGreenBoxBg, BorderStyle = BorderStyle.FixedSingle };
            var lblPlanIn = new Label { Text = "PLAN IN", AutoSize = true, Font = new Font("Arial", 9, FontStyle.Bold), ForeColor = ColorGreenBoxFg, BackColor = ColorGreenBoxBg };
            lblPlanIn.Location = new Point((planInPanel.Width - lblPlanIn.PreferredWidth) / 2, 6);
            planInPanel.Controls.Add(lblPlanIn);
            _txtPlanIn = new TextBox { Text = "0", ReadOnly = true, TextAlign = HorizontalAlignment.Center, Font = new Font("Arial", 18, FontStyle.Bold), ForeColor = ColorGreenBoxFg, BackColor = ColorGreenBoxBg, BorderStyle = BorderStyle.None, Width = 100, Location = new Point((planInPanel.Width - 100) / 2, 28) };
            planInPanel.Controls.Add(_txtPlanIn);
            topPanel.Controls.Add(planInPanel);

            var planOutPanel = new Panel { Location = new Point(830, 8), Size = new Size(180, 62), BackColor = ColorBlueBoxBg, BorderStyle = BorderStyle.FixedSingle };
            var lblPlanOut = new Label { Text = "PLAN OUT", AutoSize = true, Font = new Font("Arial", 9, FontStyle.Bold), ForeColor = ColorBlueBoxFg, BackColor = ColorBlueBoxBg };
            lblPlanOut.Location = new Point((planOutPanel.Width - lblPlanOut.PreferredWidth) / 2, 6);
            planOutPanel.Controls.Add(lblPlanOut);
            _txtPlanOut = new TextBox { Text = "0", ReadOnly = true, TextAlign = HorizontalAlignment.Center, Font = new Font("Arial", 18, FontStyle.Bold), ForeColor = ColorBlueBoxFg, BackColor = ColorBlueBoxBg, BorderStyle = BorderStyle.None, Width = 100, Location = new Point((planOutPanel.Width - 100) / 2, 28) };
            planOutPanel.Controls.Add(_txtPlanOut);
            topPanel.Controls.Add(planOutPanel);

            return topPanel;
        }

        /// <summary>Add block / Remove selected controls, right-aligned.
        /// The row-count readout and gap-is-a-break hint text that used to
        /// live here have been removed per feedback.</summary>
        private Panel BuildToolbar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = ColorBg };

            _btnCloseBlock = new Button
            {
                Text = "Close selected work",
                Size = new Size(170, 26),
                Location = new Point(FormWidth - 40 - 300 - 300 - 180, 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTactBg,
                ForeColor = ColorGray,
                Font = new Font("Arial", 8.5f, FontStyle.Bold)
            };
            _btnCloseBlock.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#cbd5e0");
            _btnCloseBlock.Click += BtnCloseBlock_Click;
            bar.Controls.Add(_btnCloseBlock);

            _btnRemoveBlock = new Button
            {
                Text = "- Remove selected",
                Size = new Size(150, 26),
                Location = new Point(FormWidth - 40 - 300 - 300, 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorRedBg,
                ForeColor = ColorRed,
                Font = new Font("Arial", 8.5f, FontStyle.Bold)
            };
            _btnRemoveBlock.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#feb2b2");
            _btnRemoveBlock.Click += BtnRemoveBlock_Click;
            bar.Controls.Add(_btnRemoveBlock);

            _btnAddBlock = new Button
            {
                Text = "+ Add block",
                Size = new Size(140, 26),
                Location = new Point(FormWidth - 40 - 140 - 300, 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorGreenBg,
                ForeColor = ColorGreen,
                Font = new Font("Arial", 8.5f, FontStyle.Bold)
            };
            _btnAddBlock.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#9ae6b4");
            _btnAddBlock.Click += BtnAddBlock_Click;
            bar.Controls.Add(_btnAddBlock);

            return bar;
        }

        private void RefreshToggleColors()
        {
            bool isDay = _currentProfile != "Night Schedule";
            _btnDay.BackColor = isDay ? ColorNavy : Color.White;
            _btnDay.ForeColor = isDay ? Color.White : ColorTranslator.FromHtml("#718096");
            _btnNight.BackColor = isDay ? Color.White : ColorNavy;
            _btnNight.ForeColor = isDay ? ColorTranslator.FromHtml("#718096") : Color.White;
        }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = ColorTranslator.FromHtml("#e2e8f0"),
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                RowTemplate = { Height = 40 },
                EnableHeadersVisualStyles = false
            };

            grid.RowsDefaultCellStyle.BackColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = ColorRowAlt;
            grid.DefaultCellStyle.Font = new Font("Arial", 12, FontStyle.Bold);
            grid.DefaultCellStyle.ForeColor = ColorTranslator.FromHtml("#2d3748");
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = ColorTranslator.FromHtml("#e2e8f0");
            grid.DefaultCellStyle.SelectionForeColor = ColorTranslator.FromHtml("#2d3748");

            _colName = grid.Columns.Add("colName", "Work");
            grid.Columns[_colName].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns[_colName].DefaultCellStyle.Padding = new Padding(10, 0, 0, 0);
            grid.Columns[_colName].FillWeight = 110;

            // START TIME: Hour dropdown + Minute dropdown.
            // The ":" spacer column has been removed.
            var colStartHour = new DataGridViewComboBoxColumn
            {
                Name = "colStartHour",
                HeaderText = "START TIME",
                // FlatStyle.Flat was stripping out the raised box + separate
                // dropdown-arrow-button look the reference picture shows -
                // Standard keeps that persistent 3D chrome, which is exactly
                // what visually says "this is a clickable input" without
                // needing any custom painting.
                FlatStyle = FlatStyle.Standard
            };

            var colStartMin = new DataGridViewComboBoxColumn
            {
                Name = "colStartMin",
                HeaderText = "",
                FlatStyle = FlatStyle.Standard
            };
            colStartMin.Items.AddRange(MinuteSteps);

            _colStartHour = grid.Columns.Add(colStartHour);
            _colStartMin = grid.Columns.Add(colStartMin);

            grid.Columns[_colStartHour].FillWeight = 100;
            grid.Columns[_colStartMin].FillWeight = 100;

            // END TIME: Hour dropdown + Minute dropdown.
            // The ":" spacer column has been removed.
            var colEndHour = new DataGridViewComboBoxColumn
            {
                Name = "colEndHour",
                HeaderText = "END TIME",
                FlatStyle = FlatStyle.Standard
            };

            var colEndMin = new DataGridViewComboBoxColumn
            {
                Name = "colEndMin",
                HeaderText = "",
                FlatStyle = FlatStyle.Standard
            };
            colEndMin.Items.AddRange(MinuteSteps);

            _colEndHour = grid.Columns.Add(colEndHour);
            _colEndMin = grid.Columns.Add(colEndMin);

            grid.Columns[_colEndHour].FillWeight = 100;
            grid.Columns[_colEndMin].FillWeight = 100;

            _colTargetIn = grid.Columns.Add("colTargetIn", "TARGET IN");
            _colTargetOut = grid.Columns.Add("colTargetOut", "TARGET OUT");
            _colTact = grid.Columns.Add("colTact", "TACT [sec]");

            grid.Columns[_colTargetIn].FillWeight = 100;
            grid.Columns[_colTargetOut].FillWeight = 100;
            grid.Columns[_colTact].FillWeight = 100;

            // Target In/Target Out ARE user-editable (unlike Tact, which is
            // computed) but a flat colored fill gives zero visual signal of
            // that - it looks exactly like Tact's flat read-only box next to
            // it, just a different hue. Grid_CellPainting below repaints
            // these two columns to look like an actual text box (white fill,
            // colored border) instead, so BackColor here only matters as the
            // background behind the real inline edit box while typing - set
            // to white so it matches the custom-painted look exactly rather
            // than flashing back to a colored fill mid-edit.
            grid.Columns[_colTargetIn].DefaultCellStyle.BackColor = Color.White;
            grid.Columns[_colTargetIn].DefaultCellStyle.ForeColor = ColorTargetInFg;
            grid.Columns[_colTargetIn].HeaderCell.Style.ForeColor = ColorTranslator.FromHtml("#9ae6b4");

            grid.Columns[_colTargetOut].DefaultCellStyle.BackColor = Color.White;
            grid.Columns[_colTargetOut].DefaultCellStyle.ForeColor = ColorTargetOutFg;
            grid.Columns[_colTargetOut].HeaderCell.Style.ForeColor = ColorTranslator.FromHtml("#bee3f8");

            grid.Columns[_colTact].ReadOnly = true;
            grid.Columns[_colTact].DefaultCellStyle.BackColor = ColorTactBg;
            grid.Columns[_colTact].DefaultCellStyle.ForeColor = ColorGray;
            grid.Columns[_colTact].HeaderCell.Style.ForeColor = ColorTranslator.FromHtml("#e2e8f0");
            grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            // CommitEdit above fires on every keystroke (not just when you
            // tab/click away), which raises CellValueChanged immediately -
            // hooking that, instead of relying only on CellEndEdit, is what
            // makes Tact/Plan In/Plan Out update live while typing a new
            // Target In/Out number, rather than only after leaving the cell.
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CellFormatting += Grid_CellFormatting;
            grid.CellPainting += Grid_CellPainting;

            grid.ShowCellToolTips = true;
            grid.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == _colTargetIn)
                    e.ToolTipText = "Click and type to set the target IN count for this block.";
                else if (e.ColumnIndex == _colTargetOut)
                    e.ToolTipText = "Click and type to set the target OUT count for this block.";
                else if (e.ColumnIndex == _colTact)
                    e.ToolTipText = "Calculated automatically from Target Out and this block's duration - not editable.";
                else if (e.ColumnIndex == _colStartHour || e.ColumnIndex == _colStartMin ||
                         e.ColumnIndex == _colEndHour || e.ColumnIndex == _colEndMin)
                    e.ToolTipText = "Click the dropdown to change this time.";
            };

            // Use the grid's own native column headers instead of a
            // hand-rolled overlay panel - two attempts at a separate
            // header panel both rendered as a squashed sliver with no
            // visible text, so this switches to the standard, well-tested
            // DataGridView header instead of continuing to patch a custom
            // one.
            grid.ColumnHeadersVisible = true;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 44;
            grid.ColumnHeadersDefaultCellStyle.BackColor = ColorNavy;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Arial", 10, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = ColorNavy;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[_colName].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns[_colName].HeaderCell.Style.Padding = new Padding(10, 0, 0, 0);

            return grid;
        }

        /// <summary>Rows with no Start/End picked yet render in light gray,
        /// matching the reference's placeholder-row treatment (its
        /// "Work 9" example) instead of looking like a real configured
        /// block.</summary>
        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[e.RowIndex];
            string start = Convert.ToString(row.Cells[_colStartHour].Value);
            string end = Convert.ToString(row.Cells[_colEndHour].Value);
            bool isPlaceholder = string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end);

            if (isPlaceholder && e.ColumnIndex != _colTargetIn && e.ColumnIndex != _colTargetOut)
            {
                e.CellStyle.ForeColor = ColorPlaceholderText;
            }
        }

        /// <summary>
        /// Two unrelated custom-paint jobs share this one event:
        /// 1) Header row: merges "START TIME"/"END TIME" visually across
        ///    their Hour+Minute column pair (see inline comments below).
        /// 2) Body rows: Target In/Target Out are genuinely editable (type
        ///    a new number in), unlike Tact, which is computed. A plain
        ///    colored fill gave both the exact same "flat colored box"
        ///    look, so a user had no visual reason to think Target In/Out
        ///    could be clicked into and typed in - only the color hue
        ///    differed from Tact's read-only box. This repaints those two
        ///    columns to look like an actual text input: white fill, a
        ///    solid colored border matching the column's theme (green/
        ///    blue), leaving Tact's flat read-only look alone as the
        ///    deliberate contrast.
        /// </summary>
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                // DataGridView has no native header colspan, so "START
                // TIME"/"END TIME" otherwise sit off-center - centered only
                // within the Hour column's own header cell, not across the
                // full Hour+Minute pair the way the reference picture shows
                // it. Fix: the Hour column's header paints as a plain blank
                // navy background, and the *Minute* column's header (which
                // paints after it, so nothing can overwrite this) draws the
                // title text centered across the union of both cells'
                // bounds - reading as one merged title spanning both
                // dropdowns.
                if (e.ColumnIndex == _colStartHour || e.ColumnIndex == _colEndHour)
                {
                    e.PaintBackground(e.CellBounds, true);
                    e.Handled = true;
                    return;
                }

                if (e.ColumnIndex == _colStartMin || e.ColumnIndex == _colEndMin)
                {
                    int hourColIndex = e.ColumnIndex == _colStartMin ? _colStartHour : _colEndHour;
                    var hourBounds = _grid.GetCellDisplayRectangle(hourColIndex, -1, true);
                    var combined = Rectangle.Union(hourBounds, e.CellBounds);

                    using (var navyBrush = new SolidBrush(ColorNavy))
                    {
                        e.Graphics.FillRectangle(navyBrush, combined);
                    }

                    string title = e.ColumnIndex == _colStartMin ? "START TIME" : "END TIME";
                    TextRenderer.DrawText(
                        e.Graphics,
                        title,
                        e.CellStyle.Font,
                        combined,
                        Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    e.Handled = true;
                    return;
                }

                return; // every other header cell: default painting is fine
            }

            if (e.ColumnIndex != _colTargetIn && e.ColumnIndex != _colTargetOut) return;

            // While the cell is actively being edited, the real inline
            // TextBox editor already sits on top of it - don't paint under
            // that, just let it draw normally.
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell != null &&
                _grid.CurrentCell.RowIndex == e.RowIndex && _grid.CurrentCell.ColumnIndex == e.ColumnIndex)
            {
                return;
            }

            e.PaintBackground(e.CellBounds, true);

            var borderColor = e.ColumnIndex == _colTargetIn
                ? ColorTranslator.FromHtml("#38a169")
                : ColorTranslator.FromHtml("#3182ce");
            var textColor = e.ColumnIndex == _colTargetIn ? ColorTargetInFg : ColorTargetOutFg;

            var inset = Rectangle.Inflate(e.CellBounds, -8, -6);
            using (var whiteBrush = new SolidBrush(Color.White))
            {
                e.Graphics.FillRectangle(whiteBrush, inset);
            }
            using (var borderPen = new Pen(borderColor, 2))
            {
                e.Graphics.DrawRectangle(borderPen, inset.X, inset.Y, Math.Max(inset.Width - 1, 0), Math.Max(inset.Height - 1, 0));
            }

            TextRenderer.DrawText(
                e.Graphics,
                Convert.ToString(e.Value),
                e.CellStyle.Font,
                inset,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            e.Handled = true;
        }

        private Panel BuildBottomPanel()
        {
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = ColorBg };

            const int leftMargin = 20;
            const int rightMargin = 20;
            const int cancelWidth = 110;
            const int adminWidth = 110;
            const int gap = 10;

            int cancelX = leftMargin;
            int adminX = cancelX + cancelWidth + gap;
            int saveX = adminX + adminWidth + gap;
            int saveWidth = FormWidth - rightMargin - saveX;

            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(cancelWidth, 40),
                Location = new Point(cancelX, 20),
                BackColor = ColorRedBg,
                ForeColor = ColorRed,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            _btnCancel.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#feb2b2");
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottomPanel.Controls.Add(_btnCancel);

            _btnAdmin = new Button
            {
                Text = "Admin",
                Size = new Size(adminWidth, 40),
                Location = new Point(adminX, 20),
                BackColor = ColorTranslator.FromHtml("#edf2f7"),
                ForeColor = ColorGray,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            _btnAdmin.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#e2e8f0");
            _btnAdmin.Click += BtnAdmin_Click;
            bottomPanel.Controls.Add(_btnAdmin);

            _btnSave = new Button
            {
                Text = "Save Changes",
                Size = new Size(saveWidth, 40),
                Location = new Point(saveX, 20),
                BackColor = ColorBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Arial", 11, FontStyle.Bold)
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += BtnSave_Click;
            bottomPanel.Controls.Add(_btnSave);

            _lblErrors = new Label
            {
                AutoSize = false,
                Width = FormWidth - 40,
                Height = 18,
                Location = new Point(20, 2),
                ForeColor = ColorRed,
                Font = new Font("Arial", 8.5f, FontStyle.Bold),
                BackColor = ColorBg,
                Text = ""
            };
            bottomPanel.Controls.Add(_lblErrors);

            return bottomPanel;
        }

        /// <summary>Opens a second PIN check, then the sensor-correction
        /// popup - matching the reference's nested Admin flow. This is
        /// deliberately independent from the PIN that gated opening this
        /// whole Settings window in the first place.</summary>
        private void BtnAdmin_Click(object sender, EventArgs e)
        {
            if (!AdminPinForm.Prompt(this))
            {
                return;
            }
            using (var form = new SensorCorrectionForm(_db))
            {
                form.ShowDialog(this);
            }
        }

        // ------------------------------------------------------------------
        //  LOAD
        // ------------------------------------------------------------------
        private void ScheduleSettingsForm_Load(object sender, EventArgs e)
        {
            _dayBlocks = LoadOrPad(_db.LoadWorkBlocks("Day Schedule"), "Day Schedule");
            _nightBlocks = LoadOrPad(_db.LoadWorkBlocks("Night Schedule"), "Night Schedule");

            _cmbLine.SelectedItem = LineChoices.Contains(_config.ActiveLine) ? _config.ActiveLine : LineChoices[0];
            _currentProfile = _config.ActiveProfile == "Night Schedule" ? "Night Schedule" : "Day Schedule";

            RefreshToggleColors();
            LoadProfileIntoGrid(_currentProfile);
        }

        private static List<WorkBlockModel> LoadOrPad(List<WorkBlockModel> blocks, string profile)
        {
            var list = new List<WorkBlockModel>(blocks);
            while (list.Count < DefaultRowCount)
            {
                int order = list.Count;
                list.Add(new WorkBlockModel
                {
                    Profile = profile,
                    BlockOrder = order,
                    Name = $"Work {order + 1}",
                    StartTime = "",
                    EndTime = "",
                    IsBreak = false,
                    TargetIn = 0,
                    TargetOut = 0
                });
            }
            return list;
        }

        // First entry is "" = blank, so a Start/End can be cleared (closes the work).
        private static string[] HoursFor(string profile) => new[] { "" }.Concat(profile == "Night Schedule" ? NightHours : DayHours).ToArray();
        
        private string EnsureMinuteItem(int col, string mm)
        {
            var c = (DataGridViewComboBoxColumn)_grid.Columns[col];
            if (!c.Items.Contains(mm)) c.Items.Add(mm);
            return mm;
        }

        private static bool TrySplit(string hhmm, out string hh, out string mm)
        {
            if (!string.IsNullOrEmpty(hhmm) && hhmm.Length == 5 && hhmm[2] == ':')
            {
                hh = hhmm.Substring(0, 2);
                mm = hhmm.Substring(3, 2);
                return true;
            }
            hh = null; mm = null;
            return false;
        }

        // ------------------------------------------------------------------
        //  PROFILE TOGGLE
        // ------------------------------------------------------------------
        private void SetProfile(string profile)
        {
            if (profile == _currentProfile) return;
            SyncGridIntoList(_currentProfile);
            _currentProfile = profile;
            RefreshToggleColors();
            LoadProfileIntoGrid(_currentProfile);
        }

        private void LoadProfileIntoGrid(string profile)
        {
            _suspendRecalc = true;
            _grid.Rows.Clear();

            var hours = HoursFor(profile);
            ((DataGridViewComboBoxColumn)_grid.Columns[_colStartHour]).Items.Clear();
            ((DataGridViewComboBoxColumn)_grid.Columns[_colStartHour]).Items.AddRange(hours);
            ((DataGridViewComboBoxColumn)_grid.Columns[_colEndHour]).Items.Clear();
            ((DataGridViewComboBoxColumn)_grid.Columns[_colEndHour]).Items.AddRange(hours);

            var list = profile == "Night Schedule" ? _nightBlocks : _dayBlocks;

            foreach (var block in list)
            {
                int rowIndex = _grid.Rows.Add();
                var row = _grid.Rows[rowIndex];
                row.Cells[_colName].Value = block.Name;

                if (TrySplit(block.StartTime, out string sh, out string sm))
                {
                    row.Cells[_colStartHour].Value = hours.Contains(sh) ? sh : null;
                    row.Cells[_colStartMin].Value = EnsureMinuteItem(_colStartMin, sm);
                }
                if (TrySplit(block.EndTime, out string eh, out string em))
                {
                    row.Cells[_colEndHour].Value = hours.Contains(eh) ? eh : null;
                    row.Cells[_colEndMin].Value = EnsureMinuteItem(_colEndMin, em);
                }

                row.Cells[_colTargetIn].Value = block.TargetIn.ToString();
                row.Cells[_colTargetOut].Value = block.TargetOut.ToString();
                row.Tag = block.IsBreak; // preserved but not user-editable here (see class remarks)
            }

            _suspendRecalc = false;

            for (int i = 0; i < _grid.Rows.Count; i++) RecomputeTact(i);
            RecalcPlanTotals();
            _grid.Invalidate();
        }

        // ------------------------------------------------------------------
        //  ADD / REMOVE BLOCK
        // ------------------------------------------------------------------
        private void BtnAddBlock_Click(object sender, EventArgs e)
        {
            int rowIndex = _grid.Rows.Add();
            var row = _grid.Rows[rowIndex];
            row.Cells[_colName].Value = NextWorkName();
            row.Cells[_colTargetIn].Value = "0";
            row.Cells[_colTargetOut].Value = "0";
            row.Tag = false;
            RecomputeTact(rowIndex);
            RecalcPlanTotals();
            _grid.ClearSelection();
            row.Selected = true;
            _grid.FirstDisplayedScrollingRowIndex = rowIndex;
        }

        /// <summary>"Work N" with the smallest N not used by any row yet.</summary>
        private string NextWorkName()
        {
            var used = new HashSet<string>();
            foreach (DataGridViewRow r in _grid.Rows)
                used.Add(Convert.ToString(r.Cells[_colName].Value));
            int n = 1;
            while (used.Contains($"Work {n}")) n++;
            return $"Work {n}";
        }

        private void BtnRemoveBlock_Click(object sender, EventArgs e)
        {
            if (_grid.Rows.Count <= 1)
            {
                _lblErrors.Text = "At least one block is required.";
                return;
            }
            var current = _grid.CurrentRow ?? (_grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null);
            if (current == null)
            {
                _lblErrors.Text = "Select a block row first, then Remove selected.";
                return;
            }
            _lblErrors.Text = "";
            _grid.EndEdit();
            _grid.CurrentCell = null;
            _grid.Rows.Remove(current);
            RecalcPlanTotals();
        }

        /// <summary>Closes the selected work without deleting the row:
        /// clears Start/End and sets both targets to 0. The row stays in the
        /// list (grey) so it can be re-opened later by picking times again.</summary>
        private void BtnCloseBlock_Click(object sender, EventArgs e)
        {
            var current = _grid.CurrentRow ?? (_grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null);
            if (current == null)
            {
                _lblErrors.Text = "Click a work row first, then Close selected work.";
                return;
            }
            _lblErrors.Text = "";
            _grid.EndEdit();
            CloseRow(current.Index, -1);
        }

        /// <summary>Resets one row to the closed state: Start + End blank,
        /// Target In/Out = 0, Tact = "-", Plan totals refreshed.
        /// skipCol = the cell that triggered the close (already blank, and
        /// possibly still being edited), so it is not written to again.</summary>
        private void CloseRow(int rowIndex, int skipCol)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex];

            bool prev = _suspendRecalc;
            _suspendRecalc = true;
            try
            {
                foreach (int col in new[] { _colStartHour, _colStartMin, _colEndHour, _colEndMin })
                    if (col != skipCol) row.Cells[col].Value = "";
                row.Cells[_colTargetIn].Value = "0";
                row.Cells[_colTargetOut].Value = "0";
            }
            finally { _suspendRecalc = prev; }

            RecomputeTact(rowIndex);
            RecalcPlanTotals();
            _grid.InvalidateRow(rowIndex);
        }

        private void SyncGridIntoList(string profile)
        {
            var list = new List<WorkBlockModel>(_grid.Rows.Count);

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                var row = _grid.Rows[i];
                string sh = Convert.ToString(row.Cells[_colStartHour].Value);
                string sm = Convert.ToString(row.Cells[_colStartMin].Value);
                string eh = Convert.ToString(row.Cells[_colEndHour].Value);
                string em = Convert.ToString(row.Cells[_colEndMin].Value);

                int.TryParse(Convert.ToString(row.Cells[_colTargetIn].Value), out int targetIn);
                int.TryParse(Convert.ToString(row.Cells[_colTargetOut].Value), out int targetOut);

                list.Add(new WorkBlockModel
                {
                    Profile = profile,
                    BlockOrder = i,
                    Name = Convert.ToString(row.Cells[_colName].Value) ?? "",
                    StartTime = (!string.IsNullOrEmpty(sh) && !string.IsNullOrEmpty(sm)) ? $"{sh}:{sm}" : "",
                    EndTime = (!string.IsNullOrEmpty(eh) && !string.IsNullOrEmpty(em)) ? $"{eh}:{em}" : "",
                    IsBreak = row.Tag is bool b && b,
                    TargetIn = targetIn,
                    TargetOut = targetOut
                });
            }

            if (profile == "Night Schedule") _nightBlocks = list;
            else _dayBlocks = list;
        }

        // ------------------------------------------------------------------
        //  LIVE TACT PREVIEW + PLAN TOTALS
        // ------------------------------------------------------------------
        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_suspendRecalc) return;

            bool isTimeCol = e.ColumnIndex == _colStartHour || e.ColumnIndex == _colStartMin ||
                              e.ColumnIndex == _colEndHour || e.ColumnIndex == _colEndMin;

            if (isTimeCol) RecomputeTact(e.RowIndex);
            if (isTimeCol || e.ColumnIndex == _colTargetIn || e.ColumnIndex == _colTargetOut)
            {
                RecalcPlanTotals();
            }
            _grid.InvalidateRow(e.RowIndex);
        }

        /// <summary>
        /// Same recalculation as Grid_CellEndEdit, but fired on every
        /// keystroke instead of only once the cell is left. CommitEdit
        /// (wired to CurrentCellDirtyStateChanged above) forces the grid to
        /// accept each character as it's typed, and that commit is what
        /// raises this event - so Tact and Plan In/Plan Out now stay in
        /// sync live while the user is still typing into Target In/Target
        /// Out, instead of jumping to the new totals only after tabbing or
        /// clicking away from the cell.
        /// </summary>
        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suspendRecalc) return;
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;

            bool isTimeCol = e.ColumnIndex == _colStartHour || e.ColumnIndex == _colStartMin ||
                              e.ColumnIndex == _colEndHour || e.ColumnIndex == _colEndMin;

            // Choosing blank in a Start or End HOUR closes the whole work:
            // the other time, both targets and Tact all reset too.
            if ((e.ColumnIndex == _colStartHour || e.ColumnIndex == _colEndHour) &&
                string.IsNullOrEmpty(Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)))
            {
                CloseRow(e.RowIndex, e.ColumnIndex);
                return;
            }

            AutoFillMinute(e.RowIndex, e.ColumnIndex);

            if (isTimeCol) RecomputeTact(e.RowIndex);
            if (isTimeCol || e.ColumnIndex == _colTargetIn || e.ColumnIndex == _colTargetOut)
            {
                RecalcPlanTotals();
            }
        }

        /// <summary>Picking an hour with no minute used to leave the time
        /// "half set", which Save read as no time at all. Now the minute
        /// becomes 00 automatically, and clearing the hour clears the minute.</summary>
        private void AutoFillMinute(int rowIndex, int colIndex)
        {
            int minCol;
            if (colIndex == _colStartHour) minCol = _colStartMin;
            else if (colIndex == _colEndHour) minCol = _colEndMin;
            else return;

            var row = _grid.Rows[rowIndex];
            string h = Convert.ToString(row.Cells[colIndex].Value);
            string m = Convert.ToString(row.Cells[minCol].Value);

            _suspendRecalc = true;
            try
            {
                if (string.IsNullOrEmpty(h)) { if (!string.IsNullOrEmpty(m)) row.Cells[minCol].Value = ""; }
                else if (string.IsNullOrEmpty(m)) row.Cells[minCol].Value = "00";
            }
            finally { _suspendRecalc = false; }
        }

        private static string RowStart(DataGridViewRow row, int colHour, int colMin)
        {
            string h = Convert.ToString(row.Cells[colHour].Value);
            string m = Convert.ToString(row.Cells[colMin].Value);
            return (!string.IsNullOrEmpty(h) && !string.IsNullOrEmpty(m)) ? $"{h}:{m}" : "";
        }

        private void RecomputeTact(int rowIndex)
        {
            var row = _grid.Rows[rowIndex];
            string start = RowStart(row, _colStartHour, _colStartMin);
            string end = RowStart(row, _colEndHour, _colEndMin);

            int minutes = TargetCalculator.BlockDurationMinutes(start, end);
            int targetOut = int.TryParse(Convert.ToString(row.Cells[_colTargetOut].Value), out int t) ? t : 0;

            row.Cells[_colTact].Value = (minutes > 0 && targetOut > 0)
                ? $"{Math.Round((minutes * 60.0) / targetOut, 1)}s"
                : "-";
        }

        private void RecalcPlanTotals()
        {
            int totalIn = 0, totalOut = 0;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                string start = RowStart(row, _colStartHour, _colStartMin);
                string end = RowStart(row, _colEndHour, _colEndMin);
                if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end) || start == end) continue;

                int.TryParse(Convert.ToString(row.Cells[_colTargetIn].Value), out int tIn);
                int.TryParse(Convert.ToString(row.Cells[_colTargetOut].Value), out int tOut);
                totalIn += tIn;
                totalOut += tOut;
            }

            _txtPlanIn.Text = totalIn.ToString();
            _txtPlanOut.Text = totalOut.ToString();
        }

        // ------------------------------------------------------------------
        //  VALIDATION + SAVE
        // ------------------------------------------------------------------
        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Commit a cell that is still being typed in / a dropdown still open.
            _grid.EndEdit();
            SyncGridIntoList(_currentProfile);

            var errors = new List<ScheduleError>();
            errors.AddRange(ScheduleValidator.Validate("Day Schedule", _dayBlocks));
            errors.AddRange(ScheduleValidator.Validate("Night Schedule", _nightBlocks));

            string lineError = _cmbLine.SelectedItem == null ? "Choose a production line." : null;

            if (errors.Count > 0 || lineError != null)
            {
                var lines = errors.Select(x => x.ToString()).ToList();
                if (lineError != null) lines.Add(lineError);

                _lblErrors.Text = lines[0] + (lines.Count > 1 ? $"  (+{lines.Count - 1} more)" : "");

                MessageBox.Show(this,
                    "Cannot save yet:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, lines.Take(10)) +
                    (lines.Count > 10 ? Environment.NewLine + $"... and {lines.Count - 10} more" : ""),
                    "Schedule not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                if (errors.Count > 0)
                {
                    var first = errors[0];
                    if (first.Profile != _currentProfile) SetProfile(first.Profile);
                    if (first.RowIndex >= 0 && first.RowIndex < _grid.Rows.Count)
                    {
                        _grid.ClearSelection();
                        _grid.Rows[first.RowIndex].Selected = true;
                        _grid.FirstDisplayedScrollingRowIndex = first.RowIndex;
                    }
                }
                return;
            }

            _lblErrors.Text = "";

            // Open works sorted by time, closed works last, closed carry no targets.
            _dayBlocks = ScheduleValidator.Normalize("Day Schedule", _dayBlocks);
            _nightBlocks = ScheduleValidator.Normalize("Night Schedule", _nightBlocks);

            _db.SaveWorkBlocks("Day Schedule", _dayBlocks);
            _db.SaveWorkBlocks("Night Schedule", _nightBlocks);

            _config.ActiveLine = _cmbLine.SelectedItem.ToString();
            _config.ActiveProfile = _currentProfile;
            _db.SaveConfig(_config);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
