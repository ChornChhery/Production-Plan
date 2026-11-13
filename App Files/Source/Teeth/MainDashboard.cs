using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Teeth
{
    /// <summary>
    /// The main shop-floor dashboard. Recreates the Python Tkinter window
    /// (top bar with Time/Date/Line/Shift, the 5x3 grid, and the Reset
    /// Result button).
    ///   Two-laptop setup: Result IN arrives over the Ethernet cable from the
    ///     Sender on laptop 1 (LinkServer); Result OUT is a barcode scan heard
    ///     system-wide (ScanDetector). Enter/Space no longer count anything.
    ///   Phase 3: (originally Enter = IN, Space = OUT) and a
    ///     background autosave timer that only writes to SQLite when a
    ///     value actually changed.
    ///   Phase 4: real elapsed-time Target math (ShiftCalculator /
    ///     TargetCalculator), Plan totals computed from the active
    ///     timeline, red/green Difference coloring, and automatic
    ///     Day&lt;-&gt;Night shift rollover that wipes the counts.
    ///   Phase 5: the "Change Schedule" button (or Ctrl+Alt+S) opens the
    ///     Schedule Settings window directly (no PIN).
    ///   Phase 6: every time the live counts get wiped (shift rollover,
    ///     manual Reset, or the app closing mid-shift) a snapshot row is
    ///     appended to the Excel log in "Log Data" via ExcelLogger.
    /// </summary>
    public partial class MainDashboard : Form
    {
        private readonly DatabaseManager _db;
        private readonly ExcelLogger _excelLogger;

        private ShiftConfigModel _config;
        private LiveCountsModel _liveCounts;

        // Phase 6: the most recently computed Target/Plan numbers, kept
        // around so a rollover/reset/close can log what Target had reached
        // right before the counts were wiped.
        private TargetResult _lastTargetResult = TargetResult.Empty;

        // Dirty-check registers, same idea as last_saved_in / last_saved_out
        // / last_saved_frozen in the Python script.
        private int _lastSavedIn = -1;
        private int _lastSavedOut = -1;
        private double _lastSavedFrozen = -1.0;

        private readonly Color _bgColor = ColorTranslator.FromHtml("#91e9ff");

        // Top bar labels
        private Panel _topPanel;
        private Label _lblTime;
        private Label _lblDate;
        private Label _lblLine;
        private Label _lblShift;
        private Label _lblElapsed;
        private Button _btnReset;
        private Button _btnChangeSchedule;
        private Label _lblLink;

        // Two-laptop link: IN comes from laptop 1 over the cable, OUT from the
        // scanner on this laptop.
        private LinkServer _link;
        private ScanDetector _scanner;
        private int _linkState = -1;          // -1 unknown, 0 down, 1 up, 2 error

        // While the Schedule window is open, counts that arrive are held here
        // and added when it closes (the window reloads the counts from the
        // database, which would otherwise erase them).
        private bool _settingsOpen;
        private int _deferredIn;
        private int _deferredOut;

        private static readonly Color ColorNavy = ColorTranslator.FromHtml("#1a365d");
        private static readonly Color ColorGray = ColorTranslator.FromHtml("#4a5568");

        // Phase 4: which shift is active right now, and its timeline
        // (WorkBlocks). Timeline is only reloaded from the DB when the
        // shift actually changes, not on every 100ms tick.
        private string _currentShiftMode;
        // Identity of the current shift (its start time). Rollover is detected
        // by this changing, not just Day<->Night, so it also works if the PC
        // slept through a whole day.
        private string _currentShiftKey;
        private List<WorkBlockModel> _timeline = new List<WorkBlockModel>();

        private static readonly Color ColorGood = ColorTranslator.FromHtml("#00cc44");
        private static readonly Color ColorBad = ColorTranslator.FromHtml("#ff3333");

        // 5x3 grid, same layout as the Python `cells[row][col]` array:
        //   row 0:        [blank] [IN]     [OUT]
        //   row 1:        [Plan]  planIn   planOut
        //   row 2:        [Target] target_in target_out   (Phase 4)
        //   row 3:        [Result] countIn  countOut
        //   row 4:        [Diff]   diffIn   diffOut        (Phase 4 colors)
        private TableLayoutPanel _grid;
        private readonly Label[,] _cells = new Label[5, 3];

        private System.Windows.Forms.Timer _clockTimer;
        private System.Windows.Forms.Timer _autosaveTimer;

        public MainDashboard(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _excelLogger = new ExcelLogger(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log Data"));

            InitializeComponent();
            BuildTopBar();
            BuildGrid();

            Load += MainDashboard_Load;
            FormClosing += MainDashboard_FormClosing;
        }

        // ------------------------------------------------------------------
        //  FORM SHELL  (hand-written instead of a .Designer.cs, since there
        //  is no Visual Studio designer available while building this - it
        //  behaves identically once opened in VS)
        // ------------------------------------------------------------------
        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "TEETH PRODUCTION PLAN";
            Icon = AppBootstrap.AppIcon;
            ClientSize = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _bgColor;
            KeyPreview = true; // belt-and-suspenders alongside ProcessCmdKey

            ResumeLayout(false);
        }

        private void BuildTopBar()
        {
            // Height matches the reference: table_frame is packed with
            // pady=(150, 10), i.e. the grid starts 150px down from the top
            // of the window in the Tkinter version.
            _topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 150,
                BackColor = _bgColor
            };
            Controls.Add(_topPanel);

            // Time/Date/Elapsed: stacked top-left, same as reference.
            // Time stays black; Date and Elapsed use the reference's gray.
            _lblTime = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 24, FontStyle.Regular),
                BackColor = _bgColor,
                ForeColor = Color.Black,
                Location = new Point(10, 10),
                Text = "Time: --:--:--"
            };
            _topPanel.Controls.Add(_lblTime);

            _lblDate = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 24, FontStyle.Regular),
                BackColor = _bgColor,
                ForeColor = ColorGray,
                Location = new Point(10, 50),
                Text = "Date: ----------"
            };
            _topPanel.Controls.Add(_lblDate);

            _lblElapsed = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 24, FontStyle.Regular),
                BackColor = _bgColor,
                ForeColor = ColorGray,
                Location = new Point(10, 90),
                Text = "Elapsed: 0 s"
            };
            _topPanel.Controls.Add(_lblElapsed);

            // Line: big bold navy, centered across the top - repositioned
            // in PositionTopLabels() since AutoSize width changes with text.
            _lblLine = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 36, FontStyle.Bold),
                BackColor = _bgColor,
                ForeColor = ColorNavy,
                Text = "Line A"
            };
            _topPanel.Controls.Add(_lblLine);

            // Shift: same big bold navy style, anchored top-right.
            _lblShift = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 36, FontStyle.Bold),
                BackColor = _bgColor,
                ForeColor = ColorNavy,
                Text = "Shift: --"
            };
            _topPanel.Controls.Add(_lblShift);

            _btnReset = new Button
            {
                Text = "Reset Result",
                Font = new Font("Arial", 12, FontStyle.Bold),
                BackColor = ColorTranslator.FromHtml("#c53030"),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 34),
                TabStop = false
            };
            _btnReset.FlatAppearance.BorderSize = 0;
            _btnReset.Click += BtnReset_Click;
            _topPanel.Controls.Add(_btnReset);

            // Visible entry point to the Schedule window, sitting directly
            // under Reset Result. Opens the schedule editor immediately
            // (OpenAdminSettings); the Ctrl+Alt+S shortcut does the same.
            _btnChangeSchedule = new Button
            {
                Text = "Change Schedule",
                Font = new Font("Arial", 12, FontStyle.Bold),
                BackColor = ColorTranslator.FromHtml("#2b6cb0"),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 34),
                TabStop = false
            };
            _btnChangeSchedule.FlatAppearance.BorderSize = 0;
            _btnChangeSchedule.Click += (s, e) => OpenAdminSettings();
            _topPanel.Controls.Add(_btnChangeSchedule);

            // Status light for the cable link to laptop 1 (updated every tick).
            _lblLink = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 20, FontStyle.Bold),
                BackColor = _bgColor,
                ForeColor = ColorGray,
                Text = "Laptop 1: ..."
            };
            _topPanel.Controls.Add(_lblLink);

            // Line's width and Shift's width both change whenever their
            // text changes (AutoSize), so a plain Anchor isn't enough -
            // recompute positions on every resize AND whenever the text
            // that affects width changes (see ClockTimer_Tick and the
            // Line-name assignments in Load / OpenAdminSettings).
            _topPanel.Resize += (s, e) => PositionTopLabels();
            PositionTopLabels();
        }

        /// <summary>Re-centers the Line label and re-right-aligns the Shift
        /// label and Reset button. Needed on panel resize and any time
        /// their AutoSize width changes (i.e. their .Text changes), since
        /// WinForms Anchors don't recompute position on the control's own
        /// size change, only on the parent's resize.</summary>
        private void PositionTopLabels()
        {
            _lblLine.Location = new Point((_topPanel.Width - _lblLine.Width) / 2, 10);
            _lblShift.Location = new Point(_topPanel.Width - _lblShift.Width - 20, 10);
            _lblLink.Location = new Point((_topPanel.Width - _lblLink.Width) / 2, 86);
            // Reset Result on top, Change Schedule directly underneath it,
            // both right-aligned under the Shift label (panel is 150px tall).
            _btnReset.Location = new Point(_topPanel.Width - _btnReset.Width - 20, 68);
            _btnChangeSchedule.Location = new Point(_topPanel.Width - _btnChangeSchedule.Width - 20, 108);
        }

        private void BuildGrid()
        {
            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                // This background only ever shows through the 1px
                // right/bottom margin gap on each cell (below) - i.e. it IS
                // the gridline color. Solid black at 1px still reads as a
                // stark, "bold"-looking line purely from color contrast
                // against the light blue cells, regardless of how thin it
                // actually is. A soft gray (matching what FixedSingle used
                // to render before, and closer to Tkinter's default
                // system-gray relief="solid" border) reads as a genuinely
                // thin, subtle gridline instead.
                BackColor = Color.FromArgb(150, 150, 150),
                ColumnCount = 3,
                RowCount = 5,
                // Not using CellBorderStyle.Single here: WinForms draws that
                // border in a light system color regardless of BackColor,
                // which is exactly the "vertical lines look black, horizontal
                // lines look gray" mismatch. Instead, cells get a small
                // Margin below so the panel's own black background shows
                // through evenly as the gridline on every side.
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
                // No outer Padding here on purpose. With BackColor black
                // this used to be invisible (blending into gridHost's own
                // black Padding(10) frame right outside it), but now that
                // this background is gray (it's the interior gridline
                // color), that same Padding(10) painted as its own visible
                // gray ring around the whole table - a second frame nobody
                // asked for, sitting between the black outer band and the
                // cells. gridHost already provides the one intended outer
                // black frame, so this panel needs zero padding of its own.
            };

            // Column 0 (row labels) can't just be an equal third like the
            // other two: at 72pt, "Difference" (the widest label) does not
            // fit in 1/3 of the window and gets silently clipped to
            // "Differe" - confirmed on a real 1920px-wide run. Tkinter
            // sidesteps this because a Label there auto-grows its column to
            // fit its own content; WinForms' TableLayoutPanel does not, so
            // we measure "Difference" at the real grid font ourselves and
            // give column 0 exactly the pixel width it needs (plus the same
            // left padding/buffer used below), then split what's left
            // evenly between the IN/OUT columns. This keeps it correct at
            // any window size instead of guessing a percentage that could
            // still clip on a different monitor.
            var gridFont = new Font("Arial", 72, FontStyle.Regular);
            int labelPadding = 30; // matches the left padding applied to row-label cells below
            int labelColumnWidth;
            using (var g = CreateGraphics())
            {
                // Deliberately NOT using TextFormatFlags.NoPadding here: a
                // real screenshot showed "Difference" still clipped to
                // "Differenc" with that flag, because WinForms' Label draws
                // its text with TextRenderer's *default* flags (which
                // include a small amount of internal glyph padding),
                // whereas NoPadding measures a narrower, tighter bound than
                // what the Label actually paints. Measuring with the same
                // default flags Label itself uses keeps this accurate.
                var measured = TextRenderer.MeasureText(g, "Difference", gridFont);
                labelColumnWidth = measured.Width + labelPadding + 60; // +60px safety buffer
            }

            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelColumnWidth));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int r = 0; r < 5; r++)
            {
                _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            }

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    // BorderStyle.FixedSingle on every cell meant two
                    // adjacent cells each drew their own border, so every
                    // interior line was actually 2 stacked 1px borders, not
                    // a true 1px line - visible as thicker-than-expected
                    // gridlines. Switching to "no border, right/bottom
                    // margin only" means each interior seam is drawn by
                    // exactly one cell's margin (showing the black panel
                    // behind), giving a genuine single-pixel line. The
                    // rightmost column and bottom row simply pick up the
                    // grid's own outer Padding(10) as their edge instead.
                    var cell = new Label
                    {
                        Dock = DockStyle.Fill,
                        AutoEllipsis = false,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = gridFont,
                        BackColor = _bgColor,
                        ForeColor = Color.Black,
                        BorderStyle = BorderStyle.None,
                        Margin = new Padding(0, 0, 1, 1),
                        Text = ""
                    };
                    _grid.Controls.Add(cell, c, r);
                    _cells[r, c] = cell;
                }
            }

            // Row/column headers - identical text to the Python grid.
            _cells[0, 1].Text = "IN";
            _cells[0, 2].Text = "OUT";
            _cells[1, 0].Text = "Plan";
            _cells[2, 0].Text = "Target";
            _cells[3, 0].Text = "Result";
            _cells[4, 0].Text = "Difference";

            _cells[1, 0].TextAlign = _cells[2, 0].TextAlign = _cells[3, 0].TextAlign = _cells[4, 0].TextAlign
                = ContentAlignment.MiddleLeft;
            _cells[1, 0].Padding = _cells[2, 0].Padding = _cells[3, 0].Padding = _cells[4, 0].Padding
                = new Padding(labelPadding, 0, 0, 0);

            // Target row and Difference row are placeholders until Phase 4
            // implements the real elapsed-time math and color coding.
            _cells[2, 1].Text = "-";
            _cells[2, 2].Text = "-";
            _cells[4, 1].Text = "-";
            _cells[4, 2].Text = "-";

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(10) };
            gridHost.Controls.Add(_grid);
            Controls.Add(gridHost);
            gridHost.BringToFront();
        }

        // ------------------------------------------------------------------
        //  LOAD / CLOSE
        // ------------------------------------------------------------------

        private void MainDashboard_Load(object sender, EventArgs e)
        {
            _config = _db.LoadConfig();
            _liveCounts = _db.LoadLiveCounts();

            // Fix for stale counts after a restart: if the saved counts belong
            // to an earlier shift, log them and start this shift at zero.
            DateTime startupNow = DateTime.Now;
            string startupKey = ShiftKeys.ForTime(startupNow);
            ResolveStaleCountsOnStartup(startupNow, startupKey);
            _currentShiftKey = startupKey;

            _lastSavedIn = _liveCounts.CountIn;
            _lastSavedOut = _liveCounts.CountOut;
            _lastSavedFrozen = _liveCounts.FrozenSeconds;

            _lblLine.Text = _config.ActiveLine;
            PositionTopLabels();

            // Phase 4: establish which shift we're in right now and load its
            // timeline before the first tick, so the very first paint already
            // shows real Target/Plan numbers instead of a flash of zeros.
            _currentShiftMode = ShiftCalculator.GetShift(DateTime.Now);
            LoadTimelineForShift(_currentShiftMode);

            RefreshResultCells();

            _clockTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            _autosaveTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _autosaveTimer.Tick += AutosaveTimer_Tick;
            _autosaveTimer.Start();

            // Two-laptop setup: listen for "+1 IN" from laptop 1 and hear the
            // barcode scanner (+1 OUT). Both are optional extras: if either
            // cannot start, the dashboard still runs and says so on screen.
            LinkSettings.Load();

            _link = new LinkServer(LinkSettings.Port);
            _link.InReceived += OnLinkIn;
            _link.Start();

            if (LinkSettings.ScanEnabled)
            {
                try
                {
                    _scanner = new ScanDetector(LinkSettings.ScanMinChars, LinkSettings.ScanMaxGapMs);
                    _scanner.ScanDetected += OnScan;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Scan hook failed: " + ex);
                }
            }

            UpdateLinkStatus();
        }

        private void MainDashboard_FormClosing(object sender, FormClosingEventArgs e)
        {
            _clockTimer?.Stop();
            _autosaveTimer?.Stop();
            if (_scanner != null) _scanner.Dispose();
            if (_link != null) _link.Dispose();

            // Always flush on close, regardless of the dirty check, so no
            // count is ever lost on shutdown - mirrors close_app() in Python.
            _db.SaveLiveCounts(_liveCounts);

            // Phase 6: if there's anything on the board, log it - closing
            // the app doesn't wipe SQLite (that only happens on the next
            // real rollover or a manual Reset), but without this the
            // Excel log would have no record at all of a shift that never
            // made it to its boundary before the app was closed.
            // Only if the counts differ from what was already logged last time,
            // so closing and reopening without counting doesn't add duplicates.
            bool unchangedSinceLastLog =
                _liveCounts.LastLoggedIn == _liveCounts.CountIn &&
                _liveCounts.LastLoggedOut == _liveCounts.CountOut;

            if ((_liveCounts.CountIn > 0 || _liveCounts.CountOut > 0) && !unchangedSinceLastLog)
            {
                if (LogShiftSnapshot("App Closed Mid-Shift", _currentShiftMode, DateTime.Now))
                {
                    SetLastLogged(_liveCounts.CountIn, _liveCounts.CountOut);
                }
            }
        }

        // ------------------------------------------------------------------
        //  KEY CAPTURE  (Ctrl+Alt+I / Ctrl+Alt+O = manual +1, Ctrl+Alt+S = schedule)
        // ------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100;
            const int WM_SYSKEYDOWN = 0x104;

            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                // Windows sends repeated WM_KEYDOWN messages while a key is
                // held. Bit 30 of lParam is set on every repeat (key was
                // already down), so a held Enter/Space counts only once: the
                // repeats are swallowed here and never reach the counters.
                bool isAutoRepeat = (msg.LParam.ToInt64() & 0x40000000L) != 0;

                // Enter and Space no longer count anything: IN comes from laptop 1
                // over the cable and OUT from the scanner (ScanDetector). They are
                // still swallowed so a scan typed while this window has focus
                // cannot "click" a focused button such as Reset Result.
                if (keyData == Keys.Enter || keyData == Keys.Space)
                {
                    return true;
                }

                // Manual fallback if the cable or the scanner is out of action.
                if (keyData == (Keys.Control | Keys.Alt | Keys.I))
                {
                    if (!isAutoRepeat) IncrementCountIn();
                    return true;
                }

                if (keyData == (Keys.Control | Keys.Alt | Keys.O))
                {
                    if (!isAutoRepeat) IncrementCountOut();
                    return true;
                }

                // Phase 5: hidden admin entry point - no visible button
                // anywhere, deliberately, so it isn't in the shop-floor
                // operator's way. Ctrl+Alt+S -> PIN prompt -> Settings.
                if (keyData == (Keys.Control | Keys.Alt | Keys.S))
                {
                    OpenAdminSettings();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------------
        //  PHASE 5: ADMIN SETTINGS ENTRY POINT
        // ------------------------------------------------------------------

        private void OpenAdminSettings()
        {
            // No PIN here: Change Schedule opens straight away. (The "Admin"
            // button INSIDE the schedule window still asks for the PIN.)
            _settingsOpen = true;
            using (var settings = new ScheduleSettingsForm(_db, _config))
            {
                try
                {
                    settings.ShowDialog(this);
                }
                finally
                {
                    _settingsOpen = false;
                }

                // Reload live counts regardless of the settings dialog's
                // own OK/Cancel result: SensorCorrectionForm is reachable
                // from inside Settings via its own "Admin" button and
                // writes straight to LiveCounts in the database, bypassing
                // this dashboard's in-memory _liveCounts entirely. Without
                // this reload, the board would keep showing (and building
                // further IN/OUT increments on top of) the stale pre-
                // correction numbers, and the next autosave tick would
                // silently overwrite the correction in the database.
                _liveCounts = _db.LoadLiveCounts();
                _lastSavedIn = _liveCounts.CountIn;
                _lastSavedOut = _liveCounts.CountOut;
                _lastSavedFrozen = _liveCounts.FrozenSeconds;

                // Counts that arrived while the window was open (cable / scanner):
                // add them now. They are not marked as saved, so autosave stores them.
                _liveCounts.CountIn += _deferredIn;
                _liveCounts.CountOut += _deferredOut;
                _deferredIn = 0;
                _deferredOut = 0;
                RefreshResultCells();

                if (settings.DialogResult == DialogResult.OK)
                {
                    // Line and/or the active shift's timeline may have
                    // changed - reload everything the grid depends on,
                    // same as PerformShiftRollover does after a rollover.
                    _config = _db.LoadConfig();
                    _lblLine.Text = _config.ActiveLine;
                    PositionTopLabels();
                    LoadTimelineForShift(_currentShiftMode);
                }
            }
        }

        private void IncrementCountIn()
        {
            if (_settingsOpen) { _deferredIn++; return; }
            _liveCounts.CountIn++;
            RefreshResultCells();
        }

        private void IncrementCountOut()
        {
            if (_settingsOpen) { _deferredOut++; return; }
            _liveCounts.CountOut++;
            RefreshResultCells();
        }

        // ------------------------------------------------------------------
        //  TWO-LAPTOP LINK  (IN from the cable, OUT from the scanner)
        // ------------------------------------------------------------------

        /// <summary>"+1 IN" arrived from laptop 1 (background thread).</summary>
        private void OnLinkIn()
        {
            try { BeginInvoke(new Action(IncrementCountIn)); }
            catch (InvalidOperationException) { /* window is closing */ }
        }

        /// <summary>The scanner typed a code + Enter (inside the keyboard hook,
        /// so hand the work to the UI loop and return at once).</summary>
        private void OnScan()
        {
            try { BeginInvoke(new Action(IncrementCountOut)); }
            catch (InvalidOperationException) { /* window is closing */ }
        }

        private void UpdateLinkStatus()
        {
            if (_link == null) return;

            int state = _link.StartError != null ? 2 : (_link.IsConnected ? 1 : 0);
            if (state == _linkState) return;
            _linkState = state;

            if (state == 1)
            {
                _lblLink.Text = "Laptop 1: Connected";
                _lblLink.ForeColor = ColorTranslator.FromHtml("#0a7a2f");
            }
            else if (state == 2)
            {
                _lblLink.Text = "Link error: port " + LinkSettings.Port + " busy";
                _lblLink.ForeColor = ColorTranslator.FromHtml("#c53030");
            }
            else
            {
                _lblLink.Text = "Laptop 1: NOT connected";
                _lblLink.ForeColor = ColorTranslator.FromHtml("#c53030");
            }
            PositionTopLabels();
        }

        // ------------------------------------------------------------------
        //  RESET
        // ------------------------------------------------------------------

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Reset IN / OUT counts and frozen time to zero?",
                "Confirm Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                return;
            }

            // Phase 6: log what was on the board before it's wiped - a
            // manual Reset is just as much a "the counts are gone now" event
            // as a shift rollover, so it gets the same treatment.
            LogShiftSnapshot("Manual Reset", _currentShiftMode, DateTime.Now);

            _liveCounts.CountIn = 0;
            _liveCounts.CountOut = 0;
            _liveCounts.FrozenSeconds = 0.0;

            _db.SaveLiveCounts(_liveCounts);
            SetLastLogged(null, null);
            _lastSavedIn = 0;
            _lastSavedOut = 0;
            _lastSavedFrozen = 0.0;

            RefreshResultCells();
        }

        // ------------------------------------------------------------------
        //  TIMERS
        // ------------------------------------------------------------------

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            _lblTime.Text = now.ToString("'Time: 'HH:mm:ss");
            _lblDate.Text = now.ToString("'Date: 'yyyy-MM-dd");

            string shiftMode = ShiftCalculator.GetShift(now);
            string shiftKey = ShiftKeys.ForTime(now);

            // --- Phase 4: automatic shift rollover ---
            // Mirrors update_gui() in the Python script: the very first tick
            // just records the shift we're in; on every tick after that, if
            // the shift changed (crossed the 08:00 or 20:00 boundary), the
            // counts are wiped and immediately persisted.
            if (_currentShiftKey != null && shiftKey != _currentShiftKey)
            {
                PerformShiftRollover(shiftMode, shiftKey, now);
            }
            _currentShiftMode = shiftMode;
            _currentShiftKey = shiftKey;
            _lblShift.Text = $"Shift: {shiftMode}";
            UpdateLinkStatus();
            PositionTopLabels();

            DateTime shiftStart = ShiftCalculator.GetShiftStart(now);
            int elapsedSeconds = (int)(now - shiftStart).TotalSeconds;
            _lblElapsed.Text = $"Elapsed: {elapsedSeconds} s";

            TargetResult result = TargetCalculator.Calculate(
                ShiftCalculator.ApplyFrozenTime(now, shiftStart, _liveCounts.FrozenSeconds), shiftStart, _timeline);
            ApplyTargetResult(result);
        }

        /// <summary>Wipes IN/OUT/FrozenSeconds when the shift boundary is
        /// crossed, saves immediately (not waiting for the autosave timer),
        /// and reloads the new shift's timeline. Phase 6: logs the shift
        /// that just ended before any of that wiping happens.</summary>
        private void PerformShiftRollover(string newShiftMode, string newShiftKey, DateTime now)
        {
            DateTime oldStart;
            if (!ShiftKeys.TryParse(_currentShiftKey, out oldStart))
            {
                oldStart = ShiftCalculator.GetShiftStart(now.AddMinutes(-1));
            }
            LogShiftSnapshot("Shift Rollover", _currentShiftMode, now, oldStart, _lastTargetResult);

            _liveCounts.CountIn = 0;
            _liveCounts.CountOut = 0;
            _liveCounts.FrozenSeconds = 0.0;
            _liveCounts.ShiftKey = newShiftKey;
            SetLastLogged(null, null);

            _db.SaveLiveCounts(_liveCounts);
            _lastSavedIn = 0;
            _lastSavedOut = 0;
            _lastSavedFrozen = 0.0;

            LoadTimelineForShift(newShiftMode);
            RefreshResultCells();

            System.Diagnostics.Debug.WriteLine($"Shift boundary crossed. Rolled over to {newShiftMode} Shift. Registers wiped.");
        }

        // ------------------------------------------------------------------
        //  PHASE 6: EXCEL LOGGING
        // ------------------------------------------------------------------

        /// <summary>
        /// Appends one row to the Excel log for whatever just happened to
        /// the live counts. Wrapped in try/catch so a locked or otherwise
        /// unwritable log file (e.g. someone has Teeth_Log.xlsx open in
        /// Excel) never takes down the dashboard - same "don't crash the
        /// shop floor" philosophy as the try/catch around Main() in
        /// Program.cs. On failure it's surfaced only to Debug output, since
        /// there's nothing a shop-floor operator could do about it anyway.
        /// </summary>
        private bool LogShiftSnapshot(string reason, string shiftModeForLog, DateTime now,
            DateTime shiftStart, TargetResult target)
        {
            try
            {
                // ShiftDate is the day the shift STARTED (a Night shift that
                // runs past midnight is logged under the day before). The
                // caller passes the shift's real start so this is right even
                // when logging happens right after a boundary.
                _excelLogger.AppendEntry(new ShiftLogEntry
                {
                    Timestamp = now,
                    ShiftDate = shiftStart.Date,
                    Line = _config.ActiveLine,
                    Shift = shiftModeForLog,
                    Reason = reason,
                    PlanIn = target.PlanIn,
                    PlanOut = target.PlanOut,
                    TargetIn = target.TargetIn,
                    TargetOut = target.TargetOut,
                    ResultIn = _liveCounts.CountIn,
                    ResultOut = _liveCounts.CountOut
                });
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Excel log failed: {ex}");
                return false;
            }
        }

        /// <summary>Log for the shift that is running right now (Manual Reset,
        /// App Closed Mid-Shift).</summary>
        private bool LogShiftSnapshot(string reason, string shiftModeForLog, DateTime now)
        {
            return LogShiftSnapshot(reason, shiftModeForLog, now,
                ShiftCalculator.GetShiftStart(now), _lastTargetResult);
        }

        /// <summary>
        /// Called once at startup. The database remembers which shift the saved
        /// counts belong to (LiveCounts.ShiftKey). If that is not the shift
        /// running now (e.g. the PC was off at 20:00), the old counts are
        /// written to the Excel log as a "Shift Rollover" row for the OLD
        /// shift, then zeroed and saved so the board starts clean.
        /// </summary>
        private void ResolveStaleCountsOnStartup(DateTime now, string currentKey)
        {
            string storedKey = _liveCounts.ShiftKey;

            if (storedKey == currentKey)
            {
                return; // counts are from this shift - nothing to do
            }

            DateTime oldStart;
            if (storedKey == null || !ShiftKeys.TryParse(storedKey, out oldStart))
            {
                // Database from before this fix (or unreadable marker): we
                // cannot know which shift the counts are from, so keep them and
                // adopt them as this shift's. Never wipe counts we can't prove
                // are stale.
                _liveCounts.ShiftKey = currentKey;
                _db.SaveLiveCounts(_liveCounts);
                return;
            }

            if (_liveCounts.CountIn > 0 || _liveCounts.CountOut > 0)
            {
                string oldMode = ShiftCalculator.GetShift(oldStart);
                var oldTimeline = _db.LoadWorkBlocks($"{oldMode} Schedule");
                // The old shift is over, so its Target is the full Plan (less any frozen time).
                TargetResult oldTarget = TargetCalculator.Calculate(
                    ShiftCalculator.ApplyFrozenTime(oldStart.AddHours(12), oldStart, _liveCounts.FrozenSeconds),
                    oldStart, oldTimeline);

                int lostIn = _liveCounts.CountIn;
                int lostOut = _liveCounts.CountOut;

                bool logged = LogShiftSnapshot("Shift Rollover", oldMode, now, oldStart, oldTarget);
                if (!logged)
                {
                    MessageBox.Show(
                        $"Counts from the previous {oldMode} shift ({oldStart:yyyy-MM-dd}) could not be " +
                        "written to Log Data\\Teeth_Log.xlsx (is it open in Excel?)." + Environment.NewLine +
                        $"IN = {lostIn}, OUT = {lostOut}. Please note these numbers down.",
                        "Teeth - log not written",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            _liveCounts.CountIn = 0;
            _liveCounts.CountOut = 0;
            _liveCounts.FrozenSeconds = 0.0;
            _liveCounts.ShiftKey = currentKey;
            _db.SaveLiveCounts(_liveCounts);
            SetLastLogged(null, null);
        }

        /// <summary>Records (or clears, with nulls) which counts the Excel log
        /// already holds as a mid-shift snapshot - in memory and in the DB.</summary>
        private void SetLastLogged(int? countIn, int? countOut)
        {
            _liveCounts.LastLoggedIn = countIn;
            _liveCounts.LastLoggedOut = countOut;
            _db.SetLastLogged(countIn, countOut);
        }

        private void LoadTimelineForShift(string shiftMode)
        {
            _timeline = _db.LoadWorkBlocks($"{shiftMode} Schedule");
        }

        private void AutosaveTimer_Tick(object sender, EventArgs e)
        {
            bool changed =
                _liveCounts.CountIn != _lastSavedIn ||
                _liveCounts.CountOut != _lastSavedOut ||
                Math.Abs(_liveCounts.FrozenSeconds - _lastSavedFrozen) > 0.0001;

            if (!changed)
            {
                return;
            }

            _db.SaveLiveCounts(_liveCounts);

            _lastSavedIn = _liveCounts.CountIn;
            _lastSavedOut = _liveCounts.CountOut;
            _lastSavedFrozen = _liveCounts.FrozenSeconds;
        }

        // ------------------------------------------------------------------
        //  GRID REFRESH
        // ------------------------------------------------------------------

        /// <summary>Updates just the Result row (IN/OUT). Called on every
        /// key press and on reset - cheap, no DB or math involved.</summary>
        private void RefreshResultCells()
        {
            _cells[3, 1].Text = _liveCounts.CountIn.ToString();
            _cells[3, 2].Text = _liveCounts.CountOut.ToString();
        }

        /// <summary>
        /// Phase 4: applies a freshly-calculated TargetResult to the Plan
        /// and Target rows, then recomputes the Difference row and its
        /// red/green coloring against the live counts. Called once per
        /// clock tick (every 100ms), same cadence as update_gui() in Python.
        /// </summary>
        private void ApplyTargetResult(TargetResult result)
        {
            // Phase 6: remember the latest figures so a rollover/reset/close
            // that happens on some later tick can log what Target/Plan had
            // reached right before the counts were wiped.
            _lastTargetResult = result;

            _cells[1, 1].Text = result.PlanIn.ToString();
            _cells[1, 2].Text = result.PlanOut.ToString();

            _cells[2, 1].Text = result.TargetIn.ToString();
            _cells[2, 2].Text = result.TargetOut.ToString();

            int diffIn = _liveCounts.CountIn - result.TargetIn;
            int diffOut = _liveCounts.CountOut - result.TargetOut;

            _cells[4, 1].Text = diffIn.ToString();
            _cells[4, 1].BackColor = diffIn < 0 ? ColorBad : ColorGood;

            _cells[4, 2].Text = diffOut.ToString();
            _cells[4, 2].BackColor = diffOut < 0 ? ColorBad : ColorGood;
        }
    }
}
