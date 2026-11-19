# Teeth Production Plan

**Short visual guide: `START-HERE_Teeth_Quick_Visual_Guide.pdf`** (main folder, next to `Teeth.exe`). Full detail: `docs\Teeth_Full_Reference_Guide.pdf`.

**One click per laptop (new, package 3).** LAPTOP 1 = the laptop with the key button (counts IN, runs TeethSender.exe). LAPTOP 2 = the laptop with the barcode scanner and the Teeth screen (counts OUT, runs Teeth.exe). You do NOT set the IP yourself: the tool sets 192.168.50.1 (laptop 1) and 192.168.50.2 (laptop 2).
1. Unzip both zips (the outer zip, then `Teeth_Project_2Laptops_FINAL.zip` inside it). 2. Copy the folder to both laptops. 3. Plug one USB-Ethernet adapter into each laptop and the cable between the adapters.
4. Laptop 2 first, then laptop 1: double-click `App Files\Tools\Run-Doctor-SelfTest.bat` (changes nothing; must say ALL CHECKS PASSED).
5. Laptop 2 first, then laptop 1: double-click `TEETH-AUTO.bat`, click Yes, type 2 (laptop 2) or 1 (laptop 1) the first time. 6. Read GREEN / YELLOW / RED; if laptop 1 says "no contact" only because laptop 2 was not ready, click `TEETH-AUTO.bat` on laptop 1 again. 7. Run the Doctor tests D1-D15 once (D1 and D2 only check what step 5 did). 8. Every day: with the automatic tool you do not start the programs by hand, they start by themselves a short time after log-on; just look at the screen. The routine is on page 15 of the Quick Guide, titled "Every day: the screen and the routine".
About the number on laptop 1: the bar shows "Teeth link: OK (IN sent since start: N)". N is only a running total of the key presses that Teeth has confirmed since TeethSender was started. It does NOT reset at midnight or at a shift change, it goes back to 0 whenever TeethSender or laptop 1 is restarted (the automatic tool restarting TeethSender does this too), and it is NOT the shift or day total. The correct totals per shift (Day 08:00-20:00, Night 20:00-08:00, by the laptop 2 clock) are Result IN and Result OUT on the Teeth screen of laptop 2 and in Log Data\Teeth_Log.xlsx. The two numbers are expected to be different. (TeethSender.exe is Build 2b: only this wording changed; Build 2 is in `App Files\Backup`.)
It also creates the scheduled task `Teeth Doctor` that repeats the check at log-on and every 5 minutes without a window. Results: `App Files\Logs\LAST-STATUS.txt`. `TEETH-CHECK-ONLY.bat` only looks; `TEETH-UNDO.bat` removes it. Missing .NET 4.8 is installed by the tool itself (Doctor 2: asks once, uses ndp48-x86-x64-allos-enu.exe from the folder or downloads it, checks the Microsoft signature; never in the silent 5-minute run). On a USB adapter that says Disconnected it switches the adapter off and on once (cannot fix a bad cable). It also checks the time zone against `TimeZone=` in `teeth-doctor.ini` (default SE Asia Standard Time) and, after a Yes, changes it and keeps the clock time. Counts safety net (Laptop 2): while Teeth runs, the Doctor copies IN/OUT to `C:\ProgramData\Teeth\counts-latest.txt` and writes changes to `App Files\Logs\counts-history.txt`; if Teeth is closed and the database shows 0 / 0 in the SAME shift, the saved counts are put back before Teeth starts (never across a shift change). Limits: the counts safety net does not restore when Teeth starts with a brand-new empty database, and Reset Result + a Teeth restart inside 5 minutes can bring old counts back. Test steps: `docs\Teeth_Two_Laptop_Plan.md` (D1-D15) and `App Files\Tools\Run-Doctor-SelfTest.bat`. Also by hand: click Run anyway if Windows warns, fix what is RED. If the tool itself fails it cannot repair itself: send `App Files\Logs\LAST-STATUS.txt` to the helper and use the manual steps. Not yet tested on a real Windows laptop (113 automatic checks (42 logic + 36 scenario + 35 static, Doctor 2) passed: `App Files\Source_Build\doctor-tests`); the manual steps in `SETUP-TWO-LAPTOPS.txt` stay as the fallback.

## Folder layout

```
(main folder, e.g. C:\Teeth)
  Teeth.exe          <- laptop 2: double-click to run (has the Teeth logo)
  TeethSender.exe    <- laptop 1: double-click to run (sends +1 IN over the cable)
  TEETH-AUTO.bat     <- BOTH laptops: one click = automatic setup / check / repair
  TEETH-CHECK-ONLY.bat, TEETH-UNDO.bat   <- look only / remove the automatic setup
  Test-Link.bat      <- laptop 1: tests the link to laptop 2
  START-HERE_Teeth_Quick_Visual_Guide.pdf   <- short guide, pictures and flows
  Teeth_Troubleshooting_Guide.pdf   <- something wrong? one page per problem
  Teeth_Rollout_Kit.pdf             <- more laptop pairs: pre-flight, checklist, record sheet
  SETUP-TWO-LAPTOPS.txt, SENDER-HOW-TO.txt
  docs\              <- Teeth_Full_Reference_Guide.pdf, AI_CONTEXT.md, plan, pictures
  Log Data\          <- Excel log of every shift (Teeth_Log.xlsx)
  App Files\         <- everything else, keep it next to Teeth.exe
      lib\           <- all .dll files (SQLite, ClosedXML, ...) + x64\ x86\ SQLite native code
      Teeth.sqlite   <- database: schedule + live counts (created on first run)
      Tools\         <- Teeth-Doctor.ps1 + Teeth-Doctor-Hidden.vbs (the automatic program), Run-Doctor-SelfTest.bat (checks this PC, changes nothing), Check-Requirements.bat, Update-App.bat, Allow-Laptop1-Firewall.bat,
                         Test-Link.ps1 (used by Test-Link.bat), Test-Port-Listener.bat,
                         Remove-Teeth-Firewall-Rules.bat
      Logs\          <- Teeth-Doctor-log.txt, LAST-STATUS.txt (created by the Doctor)
      teeth-doctor.ini <- Doctor settings (created on first run)
      Source\        <- Teeth.sln + C# source code (open in Visual Studio)
      Source_Sender\ <- TeethSender.sln + C# source code of TeethSender
      README.md      <- this file
```

## Run
Double-click `Teeth.exe`. Needs 64-bit Windows + .NET Framework 4.8 (any updated Windows 10/11).
First launch creates `App Files\Teeth.sqlite` and fills `Log Data` automatically.

## Copy to another offline PC
Copy the **whole folder** (USB stick is fine) to `C:\Teeth` on both laptops. Laptop 2: double-click `Teeth.exe`. Laptop 1: double-click `TeethSender.exe` (never run `Teeth.exe` there).
Nothing to install. Optional check on a new PC: `App Files\Tools\Check-Requirements.bat`.

## Keys / buttons
Two-laptop setup: Result **IN** comes from laptop 1 over the Ethernet cable, Result **OUT** is a barcode
scan on this laptop. `Enter` and `Space` no longer count anything. Manual fallback: `Ctrl+Alt+I` = +1 IN,
`Ctrl+Alt+O` = +1 OUT. `Reset Result` zeroes the counts. `Change Schedule` opens the schedule editor
**directly (no PIN)**; `Ctrl+Alt+S` does the same. The `Admin` button *inside* the schedule window
(Sensor Correction) still asks for the PIN. See `SETUP-TWO-LAPTOPS.txt` in the zip root.

## Developer
1. Open `App Files\Source\Teeth.sln`, set Release + x64, Rebuild Solution (NuGet restore needs internet once).
2. Close Teeth.exe, run `App Files\Tools\Update-App.bat` - it places the new `Teeth.exe` in the main folder
   and the new .dll files in `App Files\lib`.

How it works: `AppBootstrap.cs` tells the app to load its .dll files and SQLite native code from `App Files\lib`
and keeps the database in `App Files`. If the .dll files sit directly beside `Teeth.exe` (e.g. a Visual Studio
`bin` folder) that works too. An older `Teeth.sqlite` found next to `Teeth.exe` is moved into `App Files` automatically.

The logo is `Teeth.ico` (embedded in the exe via the project's ApplicationIcon). `Teeth_logo.png` is the full-size version.

Admin PIN (Sensor Correction only): constant `AdminPin` in `Source\Teeth\AdminPinForm.cs` (currently `mgss`).

---

## Change: two laptops - IN over the cable, OUT from the scanner (Step 6)
**Laptop 1** (key button + print software) runs the small `TeethSender` program (`TeethSender.exe` in the same main folder). It hears the key button's P + Enter system-wide and sends `IN <id>` over the cable;
Teeth answers `OK <id>`. Counts wait in `pending.txt` if the link is down (nothing lost), and a retry
of an id Teeth already counted is not counted twice.

**Laptop 2** (this app):
- `LinkServer.cs` listens on TCP port 5055 (`App Files\link.ini`, created on first run). `PING`/`PONG` every
  2 s drives the status line at the top: green `Laptop 1: Connected` / red `Laptop 1: NOT connected`
  (red `Link error: port 5055 busy` if the port cannot be opened).
- `ScanDetector.cs` is a system-wide keyboard hook on its own thread. A scan = at least `ScanMinChars` (4)
  keys, each within `ScanMaxGapMs` (50 ms) of the last, then Enter -> +1 OUT. Keys are never blocked, so the
  print software still receives the scan. Tune both values in `link.ini` if scans are missed or typing is counted.
- Enter/Space are still swallowed when Teeth has focus (so a scan cannot click a focused button) but count nothing.
- Counts that arrive while the Schedule window is open are held and added when it closes.
- `Tools\Allow-Laptop1-Firewall.bat` (run once as administrator) opens the port in Windows Firewall.
`Teeth.exe` and `TeethSender.exe` in the main folder of the zip are already rebuilt (Mono C# compiler, not yet run on Windows;
the old exe/guide are in `App Files\Backup`). `START-HERE_Teeth_Quick_Visual_Guide.pdf` covers the two laptops with pictures and flows. The TeethSender source is in `App Files\Source_Sender`.
The Excel log, shift rollover, Target and Difference are unchanged.

---

## Change: stale counts after a restart (Step 1)
`LiveCounts` now has a `ShiftKey` column: the start time of the shift the counts belong to (e.g. `2026-11-04 20:00`).
On startup, if it differs from the current shift, the old counts are written to the Excel log as a
"Shift Rollover" row for the OLD shift (Plan/Target = full Plan), then zeroed and saved. Older databases
get the column added automatically with no data lost; their existing counts are kept and adopted by the
current shift. Runtime rollover now also triggers on a shift-key change (not just Day<->Night).
Manual Reset / App Closed rows now use the current shift's real start date for Shift Date.
**Needs a rebuild:** run Release + x64 in Visual Studio, then `Tools\Update-App.bat`.

---

## Change: held keys count once (Step 2)
Holding `Enter` or `Space` no longer counts repeatedly: Windows keyboard auto-repeat is ignored, so one
physical press = one count. Release and press again to count again. Needs a rebuild + `Update-App.bat`.

---

## Change: blocks outside the shift are rejected (Step 3)
Save Changes in the schedule window now refuses any block that starts before, or ends after, its own shift
(Day 08:00-20:00, Night 20:00-08:00) and shows a red message, e.g. `Day Schedule row 10: 20:30-21:00 is outside the shift (08:00-20:00).`
Nothing is saved until it is fixed. Blocks already stored in an existing database are not changed; they are
flagged the next time Save is pressed.

---

## Change: no duplicate "App Closed Mid-Shift" rows (Step 5)
`LiveCounts` also remembers the IN/OUT last written to the log as "App Closed Mid-Shift" (`LastLoggedIn/Out`).
Closing the app logs only if the counts differ from that; the memory is cleared on Shift Rollover, Manual Reset
and the startup stale-count fix. If the Excel write fails nothing is remembered, so the next close tries again.
Older databases get the columns added automatically. Needs a rebuild + `Update-App.bat`.

---

## Change: "Frozen time" now works (Step 4)
Frozen time (sec), set in Sensor Correction (PIN), pauses Target: Target is calculated as if the clock were that
many seconds earlier (never before the shift start). Plan is unchanged, so Target ends the shift that many
seconds short of Plan. Example: Frozen = 600 at 09:00 on Day shift -> Target shows what it was at 08:50.
It resets to 0 on shift rollover and Reset Result, and is used when logging a stale shift at startup.
Needs a rebuild + `Update-App.bat`.

---

# Original development notes

## What's in this zip
```
Teeth.sln
Teeth/
  Teeth.csproj          <- SDK-style project, targets .NET Framework 4.8, WinForms, x64
  App.config
  Program.cs             <- entry point (Phase 1: creates "Log Data" folder + Teeth.sqlite; launches MainDashboard)
  DatabaseManager.cs      <- all SQLite access (Phase 2) + Phase 4's default WorkBlocks seed
  Models.cs               <- ShiftConfigModel / WorkBlockModel / LiveCountsModel
  MainDashboard.cs        <- Phase 3: the dashboard window; Phase 5: Ctrl+Alt+S admin entry point; Phase 6: logging hooks
  ShiftLogic.cs           <- Phase 4: ShiftCalculator + TargetCalculator (pure math, no UI)
  AdminPinForm.cs         <- Phase 5: hidden PIN gate
  ScheduleSettingsForm.cs <- Phase 5: the Settings window (Line, Day/Night, 11-row WorkBlocks editor)
  ExcelLogger.cs          <- Phase 6: appends rows to Log Data\Teeth_Log.xlsx via ClosedXML
```

## Important - I could not compile or test this here
I built this in a Linux sandbox with no Visual Studio, no .NET Framework
targeting pack, and no access to nuget.org, so I could not actually restore
packages or compile it. The code is written carefully and should build
cleanly, but **please build it once locally before trusting it** - if VS
flags anything, paste me the error and I'll fix it immediately.

## How to open it
1. Copy the whole `Teeth` folder to your Windows PC.
2. Open `Teeth.sln` in Visual Studio 2019 or newer (Community edition is fine).
   Make sure the ".NET desktop development" workload is installed.
3. Visual Studio will auto-restore the two NuGet packages the first time you
   build (`System.Data.SQLite.Core`, `ClosedXML`) - this needs an internet
   connection once. If your dev machine is offline, use
   `Tools > NuGet Package Manager > Package Manager Console` with a local
   package source, or download the `.nupkg` files elsewhere and add them as
   a local feed.
4. Build (Ctrl+Shift+B), then Run (F5).

## What it does right now
- On startup, creates a `Log Data` folder next to the .exe (empty for now -
  it gets used in Phase 6 for the daily Excel exports).
- Creates `Teeth.sqlite` next to the .exe if it doesn't exist yet, with three
  tables: `ShiftConfig`, `WorkBlocks`, `LiveCounts` (see `DatabaseManager.cs`
  for the exact schema - it mirrors the plan: ShiftConfig replaces the
  top-level keys of schedule.json, WorkBlocks replaces the `presets` array,
  LiveCounts replaces counts.json).
- Launches **MainDashboard** (Phase 3):
  - Top bar: live Time / Date, the active Line name (from `ShiftConfig`), a
    simple Day/Night Shift label, and a **Reset Result** button (asks for
    confirmation, then zeroes counts in the DB immediately).
  - The 5x3 grid, built with a `TableLayoutPanel`, same layout as the old
    Tkinter version: header row (IN / OUT), then Plan / Target / Result /
    Difference rows.
  - **Global key capture**: press **Enter** anywhere in the window to
    increment Count IN, press **Space** to increment Count OUT
    (`ProcessCmdKey` override, so it works no matter which control has
    focus - no textbox needs to be clicked first).
  - **Background autosave**: a timer checks every 3 seconds whether
    IN/OUT/FrozenSeconds actually changed, and only writes to SQLite when
    they did (same dirty-check pattern as the old Python `autosave()`).
    Closing the window always flushes once more regardless, so nothing is
    lost on exit.
  - Plan IN / Plan OUT cells are pulled live from `ShiftConfig`. Result IN /
    Result OUT reflect the live counts you just typed.

## Phase 4: Business Logic & Target Calculation
New file `ShiftLogic.cs` holds two pure, UI-free pieces of math (easy to unit
test on their own, and reused later by the Phase 5 Settings window):

- **`ShiftCalculator`** - `GetShift(now)` / `GetShiftStart(now)`, a direct
  translation of the Python `get_shift()` / `get_shift_start()` functions.
- **`TargetCalculator`** - a direct translation of the timeline-walking math
  inside the Python `update_gui()`. It chains each WorkBlock's Start/End
  time into an absolute timestamp starting from the shift's start, and
  accumulates a proportional target: full credit for a block that's already
  finished, a proportional fraction for whichever block is in progress right
  now, zero for anything not yet started. `PlanIn`/`PlanOut` are the same
  blocks' targets summed at 100% (i.e. what Target would read at the very
  end of the shift) - this replaces the flat `ShiftConfig.GlobalPlanIn/Out`
  fields that the Phase 3 grid used as a placeholder.

Wired into `MainDashboard`'s existing 100ms clock tick (same cadence as
Python's `root.after(100, update_gui)`):
- **Target row** now shows the real, live-updating target-so-far.
- **Plan row** now shows the full-shift total pulled from the timeline.
- **Difference row** is `Result - Target`, colored green (`#00cc44`) when
  `>= 0` and red (`#ff3333`) when negative - same thresholds as the Python
  version.
- **Automatic shift rollover**: when the clock crosses 08:00 or 20:00, IN /
  OUT / FrozenSeconds are wiped and saved immediately (not waiting for the
  3-second autosave), and the new shift's timeline is reloaded from
  `WorkBlocks`. This only fires after the first tick has already recorded a
  shift (so it never fires spuriously on startup).
- A small **Elapsed** label was added next to Shift, since it's directly
  useful for eyeballing that the target math lines up with time-of-day -
  this mirrors a field the original Python app had that wasn't explicitly
  called out in the phase plan's Top Bar bullet.

### A note on test data
`WorkBlocks` is populated by the Phase 5 Settings window, which doesn't
exist yet - so without *something* in that table, Target/Plan would always
read zero and Phase 4 would be untestable. `DatabaseManager` now seeds one
sample Day Schedule and one sample Night Schedule **only if the table is
completely empty** (i.e. on a brand-new database), using the same numbers
that were in your original `schedule.json`. I kept it to 11 blocks per
profile (dropping the original's trailing Work 9 / Work 10 / final break) to
match the "11-row scrolling table" the plan describes for the Phase 5
editor. Once you build Phase 5 you'll be able to edit or replace this
however you like - it will never be overwritten automatically, only seeded
once on a fresh `Teeth.sqlite`.

## Phase 5: Schedule Management Window
No visible button anywhere on MainDashboard opens this - it's deliberately
hidden from the shop floor. Press **Ctrl+Alt+S** anywhere on the dashboard
(same global-key mechanism as Enter/Space from Phase 3) to bring up:

1. **AdminPinForm** - a small modal PIN prompt. Wrong PIN just clears the
   box and lets you retry; Cancel or closing it aborts the whole flow with
   no changes. **The PIN is currently a hardcoded placeholder (`mgss`)** in
   `AdminPinForm.cs` - there's no settings table yet for an admin-changeable
   PIN. Tell me if you want that as a quick follow-up.
2. **ScheduleSettingsForm** - only opens if the PIN was correct:
   - **Line** combo box (editable - "Line A/B/C" pre-populated, but you can
     type any name), defaulted to the current `ShiftConfig.ActiveLine`.
   - **Day Schedule / Night Schedule** radio toggle switches which 11-row
     timeline the grid below is showing. Both profiles are loaded into
     memory as soon as the window opens, so flipping back and forth between
     tabs never loses an edit you haven't saved yet - nothing touches the
     database until you click Save.
   - **11-row grid**: Block Name, Start (HH:mm), End (HH:mm), Break?
     (checkbox), Target In, Target Out, and a read-only **Tact** column that
     recalculates live as you edit Start/End, using the exact same
     `TargetCalculator.BlockDurationMinutes()` the dashboard uses at
     runtime - so what you see in the editor is exactly what Phase 4's math
     will use.
   - **Validation on Save**: blank names, unparsable HH:mm times, and
     negative targets are hard errors that block saving (shown as a red
     summary at the bottom, first 3 + a count of any more). A block's Start
     not matching the previous block's End is flagged the same way as a
     "gap/overlap" warning - it won't break the math (`TargetCalculator`
     always chains off each block's own duration from the shift start, not
     its absolute Start/End), but a mismatch is almost always a typo worth
     catching before it goes live.
   - **Save** writes both profiles' 11 rows via the existing
     `DatabaseManager.SaveWorkBlocks()` (delete-then-reinsert per profile,
     already transactional from Phase 2) and the Line/active-profile choice
     via `SaveConfig()`, then closes with `DialogResult.OK`.
   - **Cancel** (or the window's close button) discards everything and
     closes with no DB writes at all.
3. Back on **MainDashboard**: if Settings closed with OK, it reloads
   `ShiftConfig` and re-fetches the current shift's timeline from the DB -
   same refresh path `PerformShiftRollover()` already uses - so edited
   Target/Plan numbers and a changed Line name show up immediately, with no
   restart needed.

### A note on validation scope
I kept validation to what actually matters for correctness (parseable
times, non-negative targets, required names) plus one soft warning for
timeline gaps/overlaps, rather than trying to block every possible
schedule-design choice - e.g. you can still make a block 1 minute long or
give a Break row nonzero targets if you really want to, since neither is
actually wrong, just unusual.

## Phase 6: Excel export / daily logging
New file `ExcelLogger.cs` appends one row to a single, ever-growing workbook
- `Log Data\Teeth_Log.xlsx`, next to `Teeth.sqlite` - every time the live
counts get wiped. There's no separate "export" button or menu: the log is
always up to date, built incrementally, the same way `Teeth.sqlite` is.

- **When it logs** - `LogShiftSnapshot()` is called from three places in
  `MainDashboard`, right before the counts are zeroed in each case:
  - **Shift Rollover** - the automatic 08:00/20:00 boundary crossing
    (Phase 4). This is the normal, expected daily log entry - two per day,
    one per shift.
  - **Manual Reset** - clicking **Reset Result** on the dashboard.
  - **App Closed Mid-Shift** - only if `FormClosing` fires with a non-zero
    count on the board, so a partial shift never vanishes just because the
    app was closed before the next boundary. Note this means if you close
    and reopen mid-shift, you'll see *two* rows for that shift once it
    finally rolls over - the mid-shift snapshot, then the final rollover
    total - which is intentional: nothing is ever silently dropped.
- **What each row contains**: Timestamp, Shift Date (the *shift's* start
  date, not necessarily today's date - a Night shift that starts at 20:00
  and rolls over after midnight is logged under the day it started),
  Line, Shift (Day/Night), Reason, Plan In/Out, Target In/Out (wherever
  Target had gotten to at the moment of logging - `_lastTargetResult`,
  updated every 100ms tick), Result In/Out, and Diff In/Out (computed at
  write time).
- **How it writes**: `ExcelLogger` has no in-memory cache - every call
  opens `Teeth_Log.xlsx` (or creates it, with a bold, frozen header row, if
  it doesn't exist yet), appends one row to the bottom of the "Shift Log"
  sheet, and saves immediately. So the file on disk is always the complete,
  current history; nothing is buffered and lost if the app crashes.
- **Failure handling**: the whole thing is wrapped in try/catch. If
  `Teeth_Log.xlsx` happens to be open in Excel (locked for writing) or
  anything else goes wrong, the dashboard keeps running exactly as normal -
  it just silently skips that one log row (visible only in Debug output).
  A logging hiccup should never be able to freeze the shop floor.

## Phase 7: Packaging for USB deployment
New file `Package-USB.bat`, at the top level next to `Teeth.sln`. It doesn't
build anything - it just finds your **Release / x64** build output and
collects the files a shop-floor PC actually needs into a clean `Teeth_USB`
folder, leaving out everything dev-only.

- **Why a script instead of an installer**: this app is deliberately
  xcopy-deployable - it has no registry entries, no install location it
  cares about, and (per the Notes section above) already builds every path
  it uses off `AppDomain.CurrentDomain.BaseDirectory`, i.e. "wherever
  `Teeth.exe` currently is." That was already true before Phase 7; this
  script just automates gathering the right files instead of you doing it
  by hand and possibly missing one.
- **What it does**:
  1. Looks for `Teeth.exe` under `Teeth\bin\x64\Release\...` (checking a
     couple of possible subfolder layouts, since SDK-style .NET Framework
     projects vary slightly by SDK version).
  2. Wipes and recreates a `Teeth_USB` folder next to the script.
  3. Copies the build output in, **excluding** `*.pdb`, `*.xml` doc files,
     `*.vshost.*`, any `Teeth.sqlite` that got created while you were
     testing in Visual Studio, and any `Log Data\` folder - you don't want
     to hand a shop-floor PC your dev test data.
  4. Checks for the files that actually matter at runtime - `Teeth.exe`,
     `Teeth.exe.config`, `System.Data.SQLite.dll`, `ClosedXML.dll`,
     `DocumentFormat.OpenXml.dll`, and `SQLite.Interop.dll` (in an `x64\`
     subfolder or next to the exe, either is fine) - and prints `[OK]` /
     `[MISSING]` for each so a missing dependency shows up immediately
     instead of as a cryptic crash on the shop floor.
- **What you do with the result**: copy the whole `Teeth_USB` folder onto
  the USB stick. On the target PC, either run it straight off the stick or
  copy the folder somewhere local first, then double-click `Teeth.exe`.
  No installer, no admin rights needed - just the folder.
- **One dependency the script can't check for you**: **.NET Framework
  4.8** has to already be on the target PC. It ships built into Windows 11
  and current Windows 10 (Windows Update installs it automatically), so on
  any machine that's had updates this needs nothing extra. If a target PC
  is old/offline enough that it's genuinely missing 4.8, grab the
  standalone installer from Microsoft
  (`ndp48-x86-x64-allos-enu.exe`, ~114 MB) and run it once on that machine.
- **Per-install data**: each copy of the `Teeth_USB` folder is fully
  independent - the first launch on a given PC creates its own
  `Teeth.sqlite` and `Log Data\Teeth_Log.xlsx` right next to `Teeth.exe`
  *on that machine*. Two lines/PCs each get their own database and log
  file; nothing is shared between them unless you deliberately point them
  at the same folder (e.g. a network share), which isn't something the app
  currently supports (no file locking across machines - stick to one
  install per PC, one PC per production line).
- **Updating a machine already in the field**: to push a code change to a
  PC that's already deployed, replace everything in its folder *except*
  `Teeth.sqlite` and `Log Data\` (that's its accumulated history) with a
  fresh `Teeth_USB` build. I'd recommend a follow-up "Update-USB.bat" if
  this becomes a recurring need - say the word and I'll add it.

### What I could and couldn't verify
Same limitation as every phase - no Windows box here, so I couldn't
actually run `Package-USB.bat` end to end. The logic (locate build output,
robocopy with excludes, check for the known dependency DLLs) is
straightforward batch/robocopy and should just work, but **please run it
once and confirm** rather than assuming.

## Deliberately not done yet
Nothing from the original phase plan remains - Phases 1 through 7 are all
in. Anything past this point (admin-changeable PIN instead of the
hardcoded `mgss`, an Update-USB script, multi-machine/network support,
etc.) would be a new ask rather than a planned phase - let me know if you
want any of it.

## Notes / decisions I made
- `PlatformTarget` is set to `x64` rather than AnyCPU, because
  `System.Data.SQLite.Core` ships separate native interop DLLs per
  architecture (x86/x64), and pinning to x64 avoids "wrong bitness" DLL
  errors on your target Windows PCs. If any of your machines are 32-bit
  Windows, tell me and I'll switch this to x86 instead.
- `WorkBlocks` stores `StartTime`/`EndTime` as `TEXT` in `HH:mm` format,
  matching what the Phase 5 settings screen will let you pick from dropdowns
  (same as the old Python combo boxes). Let me know if you'd rather store
  `duration_minutes` like the original `schedule.json` did - it's an easy
  change.
- `ShiftConfig` and `LiveCounts` are deliberately single-row tables
  (`Id INTEGER PRIMARY KEY CHECK (Id = 1)`), same "one row, always update it"
  pattern the plan describes.

## Next step
Build and run it. Since your PC's actual clock probably isn't sitting right
on 08:00 or 20:00, the easiest way to see Target/Plan/Difference update is
to just watch them for a few seconds (Target should tick upward smoothly)
and press Enter/Space a few times to see Difference flip colors. To sanity
check shift rollover without waiting for a real boundary, you can
temporarily change your PC's system clock to a couple minutes before
08:00 or 20:00 and watch the counts wipe as it crosses over.

## Next step
Same caveat as always - **I still can't compile or test this myself**, so
please verify before treating this as done:

1. Build the solution (Release, x64), and if you haven't already, re-test
   Phases 5 and 6 (Ctrl+Alt+S / PIN `mgss`; Reset Result creates/append to
   `Teeth_Log.xlsx`).
2. Run `Package-USB.bat` from the folder that contains `Teeth.sln`. Confirm
   it prints `[OK]` for every file and that `Teeth_USB\Teeth.exe` actually
   launches when run from a different folder (simulating "copied to
   another PC") - that's the real test of whether anything's missing.
3. Copy `Teeth_USB` onto an actual USB stick and try it on one target PC
   before rolling out to all of them.

Once that's confirmed, Phases 1-7 are complete and this is ready for the
shop floor.


### Link simulation (2026-11-09)
`App Files\Source_Build\link-simulation` runs the real LinkClient/LinkServer against a fake cable (maintainer tool, Linux + Mono): 13 of 13 checks passed (cable pull, lost OK reply, sender restart, Teeth closed).

### Build 2 (2026-11-08)
`Teeth.exe` and `TeethSender.exe` were rebuilt against the real .NET Framework 4.8 reference assemblies. Build 1 crashed with `Method not found: String.Split(Char, StringSplitOptions)`. See `VERSION.txt` and `App Files\Source_Build\build-with-mono.sh`.

### Version 2 note: network port
The Teeth link runs on a USB-to-Ethernet adapter on each laptop (192.168.50.1 / 192.168.50.2, no gateway), not on the built-in Ethernet port, which stays on the print network. Teeth listens on all adapters (`IPAddress.Any`), the Sender connects to `Host` in `sender.ini`, and the firewall rule is port-based (TCP 5055, all profiles, only from 192.168.50.0/24), so no code change is needed. If 192.168.50.x clashes with the print network, use another range and update `Host`, the Windows IPs and `HOST` in `Test-Link.bat`.

## Change: same QR scanned again is NOT counted again (Result OUT)
Teeth now reads the text of each scanned QR code (ScanDetector -> ScanGuard). A code already counted is refused with a red message on screen,
a beep, and a line in `App Files\Logs\Scan-Rejected.txt`. Counted codes are kept in Teeth.sqlite (table ScannedCodes), so closing Teeth does not forget them;
Reset Result forgets them. New keys in `App Files\link.ini`: DuplicateCheck (true), DuplicateScope (shift | day | always), ValidCodesFile (optional list of valid codes).
Codes are compared ignoring upper/lower case and spaces around them. Codes are remembered for 60 days at most.
Install: close Teeth.exe on laptop 2, copy the new Teeth.exe over the old one (database and Log Data are kept). Laptop 1 needs nothing. Old exe: `App Files\Backup\Teeth_before_duplicate_fix.exe`.
Tests: `App Files\Source_Build\scan-tests\run-tests.sh` (17 checks). Not yet tested on a real Windows laptop with the real scanner: see test D16-D19 in `docs\Teeth_Two_Laptop_Plan.md`.
