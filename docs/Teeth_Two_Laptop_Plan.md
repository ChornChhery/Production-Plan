# Teeth: two-laptop plan (version 2: USB-Ethernet adapters)

Result IN comes from laptop 1 over an Ethernet cable between two USB-to-Ethernet adapters. Result OUT comes from the scanner on laptop 2. Everything stays offline.

## 1. Goal

| | Laptop 1 | Laptop 2 |
|---|---|---|
| IP address (USB adapter) | `192.168.50.1` | `192.168.50.2` |
| Hardware | Key button (sends P + Enter), printer | Barcode scanner, printer, monitor |
| Software | Print software + **TeethSender** (new) | Print software + **Teeth** (updated) |
| Role | Result **IN** | Result **OUT** + dashboard |


## 1b. Version 2: why USB adapters

Both laptops already use their **built-in Ethernet port** for the print software. Putting a fixed `192.168.50.x` address with no gateway on that port could cut the laptop off the print network. So the Teeth link now uses a **second port**: one USB-to-Ethernet adapter per laptop, with the cable between the two adapters.

- The built-in port and its settings are **not changed**. Printing is not affected.
- No code change is needed (checked in the source): Teeth listens on `IPAddress.Any`, the Sender connects to `Host` from `sender.ini`, and the firewall rule is port-based (TCP 5055, all profiles, only from 192.168.50.0/24).
- Set the IP on the **USB adapter only** (`ncpa.cpl`, usually named "Ethernet 2"): mask `255.255.255.0`, gateway and DNS empty.
- Check first with `ipconfig /all` that the print network does not already use `192.168.50.x`. If it does, use another range (for example `10.10.10.1` and `10.10.10.2`) and update: Windows IPs, `Host` in `sender.ini`, and `HOST` in `Test-Link.bat`.
- Always use the same USB port. A different port can look like a new adapter with default settings.
- If the link drops after idle time, turn off "Allow the computer to turn off this device to save power" for the adapter (Device Manager, Power Management).

Test layers (fix the lowest failing layer first): 1 cable and port light, 2 `ipconfig`, 3 `ping` (Windows may block ping by default), 4 port test (`Test-Link.bat` or `Test-NetConnection 192.168.50.2 -Port 5055`), 5 green bar and "Connected", 6 key button and scanner.

## 1c. Status on the real laptops (2026-11-07)

**Phase 1 passed** on the owner's two laptops, without any Teeth software: restore points saved; USB adapters set to `192.168.50.1` (laptop 1, adapter "Ethernet 4") and `192.168.50.2` (laptop 2, adapter "Ethernet 2"); port 5055 free on laptop 2; two firewall rules added on laptop 2 (`Teeth link` TCP 5055 and `Teeth ping`, both only from `192.168.50.0/24`, both answered Ok.); ping laptop 1 to laptop 2 works after the rules (it timed out before: Windows blocks ping by default; ARP already showed laptop 2, so the cable was fine); a stand-in listener on laptop 2 answered `Test-NetConnection 192.168.50.2 -Port 5055` from laptop 1 with `TcpTestSucceeded : True` via the USB adapter; the key button types `p` + new line and the scanner types its code + new line in Notepad; printing works on both laptops. The print network (`192.168.0.x` /22 and `192.168.3.x`) does not clash with `192.168.50.x`.

**Phase 2, first run (2026-11-08):** `Teeth.exe` build 1 showed `Unhandled exception ... Method not found: String.Split(Char, StringSplitOptions)` on both laptops at step 18 (Check-Requirements passed). Cause: build 1 was compiled against the wrong class library; fixed in **Build 2 (2026-11-08)**: source corrected and both exes rebuilt against the .NET Framework 4.8 reference assemblies (`VERSION.txt`, `App Files/Source_Build/build-with-mono.sh`).

**Build 2 confirmed by the owner (2026-11-08): it works** on both laptops (steps 16-24). Not separately confirmed: the startup shortcut and the full list of 12 acceptance tests (shift change, outage and restart cases).

## 1d. Case log (problems met on pair 1 and their fixes)

1. First Test-Link run: laptop 1 ping 100% loss + NOT OK; laptop 2 (run there by mistake) ping reply + port closed. Causes: Windows blocks inbound ping by default (no ping rule); Teeth was not running; on laptop 2 the ping went to itself. Fix: the two firewall rules (step 8), open Teeth first, Test-Link on laptop 2 then laptop 1; the new Test-Link detects which laptop it is and says so.
2. ipconfig on both laptops was correct (USB adapters "Ethernet 4" = 192.168.50.1 and "Ethernet 2" = 192.168.50.2, no gateway); laptop 2 could ping laptop 1 but not the reverse; `arp -a` on laptop 1 already listed laptop 2's physical address, so the cable worked and only inbound ping on laptop 2 was blocked.
3. Owner asked whether the firewall commands affect the existing Ethernet/print software: no (incoming allow rules only). The rules were tightened to `remoteip=192.168.50.0/24`; restore points and `Remove-Teeth-Firewall-Rules.bat` added.
4. Phase 1 proof without our software passed (listener + `Test-NetConnection` True via the USB adapter, key button `p`+newline, scanner code+newline, printing).
5. Step 18 on both laptops with build 1: `Unhandled exception ... Method not found: String.Split(Char, StringSplitOptions)`; Plan/Target empty. Cause: wrong class library at build time. Fix: Build 2 (2026-11-08). Owner confirmed it works.
6. Navigation problem: the first setup map did not say which laptop does each step. Fix: 24-step maps (Phase 1 without software, Phase 2 with our programs), every step labelled LAPTOP 1/LAPTOP 2/BOTH.

Each case has a page with a picture and copy-paste commands in `Teeth_Troubleshooting_Guide.pdf` (problems 1, 2, 10, 11, 12, FAQ and case log).

## 1e. When the cable is unplugged or moved (verified by simulation)

The real link code was run against a fake cable (`App Files/Source_Build/link-simulation`, 13 of 13 checks passed): IN counts wait on laptop 1 in `pending.txt` while the cable is out, the link turns green again about a second after plug-in (Windows may add a few seconds) and the counts arrive exactly once; a lost OK reply does not double count; waiting counts survive a restart of laptop 1 and wait while Teeth is closed. OUT (scanner) never uses the cable. **Not protected:** IN presses while `TeethSender.exe` is not running, and OUT scans while `Teeth.exe` is closed (add them with Ctrl+Alt+I / Ctrl+Alt+O). A USB adapter moved to another USB port may need its IP set again. See `Teeth_Troubleshooting_Guide.pdf` problem 14 and page 15.

## 1f. One-click setup and automatic repair: Teeth Doctor (package 3, 2026-11-11)

Why: typing the commands of steps 1-15 and the troubleshooting guide by hand is slow and error-prone, and a link that drops should repair itself without anyone touching the laptops.

LAPTOP 1 = the laptop with the key button (counts IN, runs TeethSender.exe). LAPTOP 2 = the laptop with the barcode scanner and the Teeth screen (counts OUT, runs Teeth.exe).

How it is used (the owner does NOT set the IP by hand: the Doctor sets 192.168.50.1 on laptop 1 and 192.168.50.2 on laptop 2):
1. Unzip both zips (the outer zip, then `Teeth_Project_2Laptops_FINAL.zip` inside it); never run from inside a zip.
2. Copy the unzipped folder to both laptops (the first click copies it to `C:\Teeth`).
3. Plug one USB-Ethernet adapter into each laptop (same USB port always) and put the cable between the two adapters.
4. Laptop 2 first, then laptop 1: double-click `App Files\Tools\Run-Doctor-SelfTest.bat` (changes nothing; must say ALL CHECKS PASSED).
5. Laptop 2 first, then laptop 1: double-click `TEETH-AUTO.bat`, click Yes, type 2 (laptop 2) or 1 (laptop 1) the first time; answer `y` if it asks to update an old `C:\Teeth`.
6. Read the last big line: **GREEN** (ready), **YELLOW** (works, read the notes) or **RED** (a problem it cannot fix, with a plain instruction). If laptop 1 says "no contact" only because laptop 2 was not ready yet, click `TEETH-AUTO.bat` on laptop 1 again. The result is also saved in `App Files\Logs\LAST-STATUS.txt`.
7. Run the Doctor tests D1-D15 once (D1 and D2 only check what step 5 did; D3-D15 are extra tests; D10-D15 are new in Doctor 2). From then on use the copy in `C:\Teeth`.
8. Every day: with the automatic tool you do not start the programs by hand, they start by themselves a short time after log-on; just look at the screen. The routine is on page 15 of the Quick Guide, titled "Every day: the screen and the routine".

| What | Setup (first click) | Automatic repair (task `Teeth Doctor`, every log-on and every 5 min, no window) |
|---|---|---|
| Copy to `C:\Teeth`, unblock files, restore points | yes | - |
| IP on the USB adapter only (192.168.50.1 / .2, /24, no gateway, no DNS); finds the adapter again by MAC after a move to another USB port | yes | yes |
| Refuses to touch the built-in port, Wi-Fi, an adapter with a gateway or with an address from another network | yes | yes |
| USB adapter power saving and USB selective suspend off | yes | yes |
| Laptop 2: firewall rules `Teeth link` and `Teeth ping` (only from 192.168.50.0/24) | yes | yes (recreated if missing) |
| Laptop 2: leftover test listener on port 5055 stopped | yes | yes |
| Start `Teeth.exe` (laptop 2) / `TeethSender.exe` (laptop 1) if closed | yes | yes |
| Link test with a plain-words diagnosis (cable signal / Teeth not listening / no contact) | yes | yes (logged) |
| Laptop 1: counts waiting in `pending.txt`; laptop 2: is laptop 1 connected | shown | logged |
| Warnings: PC sleeps, clock | shown | logged |
| Creates the scheduled task | yes | re-creates it if it was deleted |

Cannot be fixed by a program: cable or adapter unplugged or broken, wrong clock, company IT policy that blocks firewall rules or scheduled tasks, antivirus blocking the programs. IN presses made while `TeethSender.exe` is closed are still lost; the Doctor only limits the time it stays closed to at most 5 minutes.

What the owner still does by hand: unzip both zips (outer, then `Teeth_Project_2Laptops_FINAL.zip`), plug adapters and cable, click Yes, type 1 or 2 once, install .NET 4.8 if missing (since Doctor 2 the tool installs it itself, see the update at the end), click Run anyway if SmartScreen/antivirus warns, fix what is RED. If the Doctor itself fails it cannot repair itself: the owner sends `LAST-STATUS.txt` (and `Teeth-Doctor-log.txt`) and works with the manual steps meanwhile.

Other files: `TEETH-CHECK-ONLY.bat` (look only, changes nothing), `TEETH-UNDO.bat` (removes task, firewall rules, adapter address, suspend setting; keeps programs, database, Log Data). Settings: `App Files\teeth-doctor.ini` (`RestartPrograms=false` stops the automatic restart of the programs; `WatchEveryMinutes`).

**Status: 113 automatic checks here (42 logic, 36 simulated-PC scenarios, 35 static; all passed, Doctor 2 of 2026-11-12) in `App Files\\Source_Build\\doctor-tests` (`Test-Doctor-Logic.ps1`, `Test-Doctor-Scenarios.ps1` with `Simulate-Doctor.ps1`, `Test-Doctor-Static.ps1`); on a PC run `App Files\\Tools\\Run-Doctor-SelfTest.bat` first (changes nothing). NOT yet run on a real Windows laptop.** The 24 manual steps remain the proven fallback. Run the Doctor acceptance tests D1-D9 below.

About the number on laptop 1: the bar shows "Teeth link: OK (IN sent since start: N)". N is only a running total of the key presses that Teeth has confirmed since TeethSender was started. It does NOT reset at midnight or at a shift change, it goes back to 0 whenever TeethSender or laptop 1 is restarted (the automatic tool restarting TeethSender does this too), and it is NOT the shift or day total. The correct totals per shift (Day 08:00-20:00, Night 20:00-08:00, by the laptop 2 clock) are Result IN and Result OUT on the Teeth screen of laptop 2 and in Log Data\Teeth_Log.xlsx. The two numbers are expected to be different. TeethSender.exe is Build 2b: only this wording changed (Build 2 is kept in `App Files\Backup`).

## 2. How it works

```
Laptop 1                                    Laptop 2
Key button -> P + Enter                     Scanner -> code + Enter
   |-> Print software prints label             |-> Print software prints label
   '-> TeethSender hears P + Enter             '-> Teeth hears the scan  -> +1 OUT
          |
          |  "IN <id>"  ---- Ethernet cable ---->  Teeth  -> +1 IN
          |  "OK <id>"  <---------------------------  (reply = delivered)
          |  "PING"     ----------------------->  "PONG" (green / red light)
```

- The Sender never blocks keys. The print software still receives P + Enter.
- Every IN has a unique id. Teeth replies `OK <id>`, and a retried id is not counted twice.
- If the cable is out or Teeth is closed, counts wait in `pending.txt` on laptop 1 and are sent later.
- Teeth shows **Laptop 1: Connected** (green) or **NOT connected** (red) at the top of the screen.
- A scan is 4 or more characters typed very fast and ended with Enter. Normal typing is too slow to match.

## 3. Status

| Item | Status |
|---|---|
| Cable + USB adapters between the two laptops | **Done, proven** (2026-11-07) |
| Static IPs on the USB adapters, ping, port 5055 test | **Done, proven** |
| Firewall rules (`Teeth link`, `Teeth ping`) on laptop 2 | **Done, proven** |
| Key button and scanner type as expected (Notepad) | **Done, proven** |
| Print test with the adapters in place | **Done, proven** |
| Teeth listener, status light, scan detection | Written, compiles |
| TeethSender (laptop 1) | Written, compiles |
| Cable messaging logic (counting, retry, queue, restart) | Tested with a fake sender and listener |
| Keyboard hooks (key button, scanner) | **Not tested on real hardware yet** |
| `Teeth.exe`, `TeethSender.exe` | Rebuilt and included (Mono compiler, **not yet run on Windows**) |
| `START-HERE_Teeth_Quick_Visual_Guide.pdf` | Final version 2: short guide, pictures and flows (full detail: `docs/Teeth_Full_Reference_Guide.pdf`) |

## 4. Steps to do

The master order is the 24 steps in `START-HERE_Teeth_Quick_Visual_Guide.pdf` (maps on pages 2 and 3): Phase 1 = steps 1-15 (no software, done), Phase 2 = steps 16-24 (our programs). Every step says LAPTOP 1, LAPTOP 2 or BOTH. The checklists below are the same work grouped by laptop. `Test-Link.bat` is run on both laptops: laptop 2 first (after Teeth is open), then laptop 1.

### Step 0: Protect printing (both laptops)
- [ ] Run `ipconfig /all` and save a screenshot of the built-in Ethernet settings
- [ ] Print one label to confirm printing works
- [ ] If 192.168.50.x was set on the built-in port earlier, set it back to the saved values
- [ ] Plug in a USB-Ethernet adapter on each laptop, cable between the adapters
- [ ] Set the IP on the USB adapter only (see section 1b), then `ping` and the port test

### Step 1: Copy the folder to both laptops
- [ ] Copy the WHOLE unzipped folder (it holds `Teeth.exe`, `TeethSender.exe`, `App Files`, `Log Data`, `Test-Link.bat`) to `C:\Teeth` on BOTH laptops
- [ ] Laptop 2 runs only `Teeth.exe`; laptop 1 runs only `TeethSender.exe`
- [ ] To keep your old schedule and history, keep your old `App Files\Teeth.sqlite` and `Log Data`
- [ ] The previous exe and guide are in `App Files\Backup` if you ever need to go back

### Step 2: Open the firewall (laptop 2, once)
- [ ] Right-click `App Files\Tools\Allow-Laptop1-Firewall.bat` and choose **Run as administrator**
- [ ] It should print "Done"

### Step 3: Start Teeth (laptop 2)
- [ ] Double-click `Teeth.exe`
- [ ] The top of the screen should say **Laptop 1: NOT connected** in red (normal until the Sender runs)
- [ ] `App Files\link.ini` is created on first run

### Step 4: Install TeethSender (laptop 1)
- [ ] The folder is already on laptop 1 from Step 1 (`TeethSender.exe` is in its main folder)
- [ ] Run `Test-Link.bat` and confirm **RESULT: OK**
- [ ] Double-click `TeethSender.exe`. A bar appears at the bottom-right of the screen (green `Teeth link: OK (IN sent since start: N)`; N is only a running total since TeethSender started, not the shift total)
- [ ] Press `Win + R`, type `shell:startup`, and put a shortcut to `TeethSender.exe` there so it starts with Windows

### Step 5: Check the key button
- [ ] Press the key button once
- [ ] Right-click the Sender's tray icon and choose **Show last keys (test)**
- [ ] The list should show `P`, then `Enter` a few hundred ms apart (the exact keys are in the list)

## 5. Acceptance tests

Run these with both laptops working.

| # | Test | Expected result |
|---|---|---|
| 1 | Start the Sender while Teeth is open | Teeth turns green within a few seconds; the Sender bar turns green |
| 2 | Press the key button on laptop 1 | Result IN goes up by 1; the label prints as before |
| 3 | Scan one item on laptop 2 | Result OUT goes up by 1; the label prints as before |
| 4 | Hold the scanner trigger or scan quickly 5 times | OUT goes up by exactly 5 |
| 5 | Type normal text (for example a name) on laptop 2 | OUT does not change |
| 6 | Unplug the cable and press the key button 3 times | Both screens turn red; the Sender bar shows `waiting: 3` |
| 7 | Plug the cable back in | Both turn green; IN goes up by 3, once each |
| 8 | Close Teeth, press the button twice, reopen Teeth | The 2 counts are delivered after it reopens |
| 9 | Restart laptop 1 while counts are waiting | Waiting counts are still sent after it restarts |
| 10 | Let a shift change (08:00 or 20:00) happen | Counts reset and a row is added to `Log Data\Teeth_Log.xlsx` as before |
| 11 | Open Change Schedule, press the button during it, then close | The count appears after the window closes |
| 12 | Print one label from each laptop after the adapters are set up | Printing works exactly as before |

### Doctor acceptance tests (new, to be run once on the real laptops)

| # | Test | Expected result |
|---|---|---|
| D1 | Laptop 2, after its first `TEETH-AUTO.bat` click (step 5): look at the result | Ends GREEN or YELLOW; the USB adapter has 192.168.50.2; `Teeth link` and `Teeth ping` rules exist; Teeth is open; Task Scheduler lists `Teeth Doctor`; built-in port unchanged (compare with `network-before-teeth.txt`) |
| D2 | Laptop 1, after its first `TEETH-AUTO.bat` click (step 5): look at the result | GREEN; the USB adapter has 192.168.50.1; TeethSender open, bar green, Teeth shows Connected; key button still counts IN |
| D3 | Laptop 2: close Teeth.exe, wait up to 5 min | Teeth opens again by itself; no window flashes; the scanner still works |
| D4 | Laptop 1: close TeethSender (tray > Exit), wait up to 5 min | TeethSender opens again by itself; no window flashes |
| D5 | Laptop 2: delete the `Teeth ping` rule (`Remove-Teeth-Firewall-Rules.bat`), wait up to 5 min | Both rules are back |
| D6 | Either laptop: set the USB adapter to automatic (DHCP), or plug it into another USB port, wait up to 5 min | The 192.168.50.x address is set again; link green again |
| D7 | Unplug the cable, press the key button 3 times, wait, plug the cable in | `LAST-STATUS.txt` shows RED with the cable text while out; GREEN after; IN rises by 3 once each (as test 7) |
| D8 | Run `TEETH-CHECK-ONLY.bat` | It reports and changes nothing (IP, rules, programs unchanged) |
| D9 | Print one label from each laptop after D1/D2, and after `TEETH-UNDO.bat` | Printing works exactly as before; after Undo the task, the two rules and the adapter address are gone and the programs, database and Log Data are untouched |
| D10 | PC with .NET 4.7 (the old laptop 1): run `TEETH-AUTO.bat` | It asks once, installs .NET 4.8 (5 to 15 min), shows `[FIXED] .NET Framework 4.8` or a RESTART note; after a restart and a second run: `[OK] .NET Framework 4.8` |
| D11 | The same PC before the install: run `TEETH-CHECK-ONLY.bat` | `[PROBLEM] .NET Framework 4.8`, nothing is installed. The silent 5-minute run never installs either |
| D12 | Laptop 1, cable out, run `TEETH-AUTO.bat` by hand | It tries to switch the USB adapter off and on once, then ends RED with the cable text (bad cable or adapter); with the cable in: GREEN |
| D13 | Laptop 2 with a wrong time zone: run `TEETH-AUTO.bat` and answer Y | Time zone becomes the one in `teeth-doctor.ini` (`TimeZone=`), the clock time stays the same; the silent run alone only shows a yellow note |
| D14 | Laptop 2: press IN/OUT a few times, wait 5 minutes (`C:\ProgramData\Teeth\counts-latest.txt` shows the numbers), press Reset Result, close Teeth, run `TEETH-AUTO.bat` within 5 minutes | `[FIXED] Counts`: the old numbers come back before Teeth starts (this is the known limit used as a test). Press Reset Result again afterwards |
| D15 | Laptop 2, after D14: open `App Files\Logs\counts-history.txt` | Lines with time, IN, OUT and shift; a line `RESTORED by the Doctor` |

## 6. Settings

| File | Key | Default | Meaning |
|---|---|---|---|
| `App Files\link.ini` | `Port` | `5055` | Port Teeth listens on |
| | `LinkTimeoutSeconds` | `6` | Silence before the screen shows NOT connected |
| | `ScanEnabled` | `true` | Count scans as OUT |
| | `ScanMinChars` | `4` | Minimum characters before Enter to count as a scan |
| | `ScanMaxGapMs` | `50` | Maximum ms between keys of a scan |
| `sender.ini` (next to `TeethSender.exe`) | `Host` | `192.168.50.2` | IP of laptop 2 |
| | `Port` | `5055` | Must match Teeth's `Port` |
| | `KeyWindowMs` | `500` | Enter must follow P within this time |

If you change `Port`, also change `PORT` in `Allow-Laptop1-Firewall.bat` and run it again.

## 7. Manual keys on Teeth

| Key | Action |
|---|---|
| `Enter`, `Space` | Count nothing (the scanner and the cable do the counting) |
| `Ctrl+Alt+I` | +1 IN (fallback if the cable is out) |
| `Ctrl+Alt+O` | +1 OUT (fallback if the scanner is out) |
| `Ctrl+Alt+S` | Change Schedule |

## 8. Troubleshooting

| Problem | Check |
|---|---|
| Anything below | **First double-click `TEETH-AUTO.bat` on both laptops** (or `TEETH-CHECK-ONLY.bat` to only look). Send `App Files\Logs\LAST-STATUS.txt` if it ends RED. |
| Both red, Sender bar red | USB adapter and cable in on both laptops? Link lights on? IP set on the USB adapter (not the built-in port)? |
| Test-Link on laptop 2 shows ping replies but NOT OK | It pings its own address. On laptop 2 it only tests that Teeth is listening: start Teeth.exe and run it again. Run the cable test on laptop 1. |
| Ping times out but cable and IPs look right | Windows blocks ping by default. Re-run `Allow-Laptop1-Firewall.bat` as administrator (it now allows ping on the cable network); the port test is what matters. |
| Link drops after idle | Turn off USB power saving for the adapter |
| Printing stopped after setup | Built-in port was changed: restore the saved settings |
| `Test-Link.bat` ping fails | IP addresses typed exactly; ping allowed in the firewall |
| Ping works but `PORT CLOSED` | Teeth not running on laptop 2, or the firewall script not run |
| Teeth shows `Link error: port 5055 busy` | Another program uses the port; change `Port` in both ini files |
| Sender bar says `Key hook NOT running` | Close and restart TeethSender; check antivirus is not blocking it |
| Key button pressed but IN does not go up | Use **Show last keys (test)** and check P then Enter is listed |
| Scans missed (OUT too low) | Raise `ScanMaxGapMs` in `link.ini` to 80 |
| Typing counted as OUT | Lower `ScanMaxGapMs` or raise `ScanMinChars` |

## 9. Known limits

- Counts held during a cable outage are counted in the shift running when they arrive. An outage across a shift change puts them in the new shift.
- Teeth remembers the last 5000 ids in memory only. After a Teeth restart, an IN that was counted but whose reply was lost could be counted twice. This is very rare.
- Scan detection assumes the scanner types like a keyboard (code, then Enter).
- Antivirus software can flag keyboard hooks. Test on the real laptops early.
- The USB adapter must keep its IP settings: use the same USB port every time.
- Only one install per laptop. The two Teeth databases are never shared.

## 10. Open questions

1. Does the print software on laptop 2 print straight from the scan, or does it need a separate button?
2. Should OUT count the scan itself, or only after a label is really printed? (Counting real print jobs is more accurate but needs the Windows print log enabled by an administrator.)
3. Should the Excel log record the scan or print detail (for example the code) as well as the counts?
4. Should a supervisor be able to subtract a wrong count (for example a Sensor Correction field for IN and OUT)?
5. Does the print network already use `192.168.50.x`? (If yes, choose another range.)
6. How is each printer connected: USB, Ethernet to the printer, or company network?

## 11. Files in the package

Everything you run is in the **main folder** of the zip. Copy the whole folder to `C:\Teeth` on both laptops. Laptop 2 runs `Teeth.exe`, laptop 1 runs `TeethSender.exe`. Set the date, time and time zone by hand on both laptops (shifts follow the laptop 2 clock).

```
Teeth_Project_2Laptops_FINAL.zip
  Teeth.exe                         laptop 2: counts OUT, dashboard
  TeethSender.exe                   laptop 1: counts IN from the key button
  TEETH-AUTO.bat                    both laptops: ONE CLICK setup + automatic check and repair (new, untested on Windows)
  TEETH-CHECK-ONLY.bat / TEETH-UNDO.bat   look only / remove the automatic setup
  Test-Link.bat                     laptop 1: link test (ping + port 5055)
  START-HERE_Teeth_Quick_Visual_Guide.pdf   short guide: pictures and flows (follow this)
  Teeth_Troubleshooting_Guide.pdf   one page per problem: picture, commands, fix, undo
  Teeth_Rollout_Kit.pdf             more laptop pairs: pre-flight check, 24-step checklist, record sheet
  SETUP-TWO-LAPTOPS.txt   SENDER-HOW-TO.txt
  Log Data/                         Teeth_Log.xlsx (Excel log)
  App Files/
    lib/                            .dll files (SQLite, ClosedXML, ...)
    Teeth.sqlite, link.ini          created on first run
    README.md
    Backup/                         previous exe, v1 guide, v1 plan
    Tools/                          Allow-Laptop1-Firewall.bat, Check-Requirements.bat, Update-App.bat
    Source/                         Teeth.sln + Teeth code (LinkSettings.cs LinkServer.cs ScanDetector.cs new, MainDashboard.cs updated)
    Source_Sender/                  TeethSender.sln + TeethSender code (Program.cs LinkClient.cs PrintKeyDetector.cs SenderSettings.cs)
  docs/                             Teeth_Full_Reference_Guide.pdf (17 pages), AI_CONTEXT.md, this plan, images/
```

## Update 2026-11-12 - Doctor 2 (newer than the text above)

Changes to `App Files\Tools\Teeth-Doctor.ps1` (run by TEETH-AUTO.bat and by the scheduled task every 5 minutes). Tested only in the simulator (35 static, 42 logic, 36 scenario checks), NOT yet on a real Windows laptop.

| Problem | Doctor 2 behaviour |
|---|---|
| .NET Framework 4.8 missing (old PC with 4.7) | Installs it itself, only in an interactive run (never in the silent 5-minute run). Asks once. Installer `ndp48-x86-x64-allos-enu.exe` is taken from the Teeth folder or any drive root, or downloaded from Microsoft (`go.microsoft.com/fwlink/?linkid=2088631`, link from memory: verify). Runs only if the Authenticode signature is Valid and the signer is Microsoft Corporation. Runs `/q /norestart`. Exit 0/3010/1641 = success (restart may be needed), 5100 = Windows too old (run Windows Update), 1603/1618 = blocked by another install or pending restart. |
| Laptop 1 USB adapter "Disconnected" | One `Restart-NetAdapter` try (interactive run only), then re-test the port. A bad cable or adapter cannot be fixed by software: swap cable, adapter, USB port; test on a PC that works. |
| Wrong time zone | New ini key `TimeZone=` (default `SE Asia Standard Time`, written at Setup, added at the next interactive Fix if missing). Different time zone = yellow [NOTE]; an interactive run asks, then `Set-TimeZone` and `Set-Date` with the previous wall-clock time (the clock shown stays the same). The silent run never changes it. |
| Counts back to 0 (Laptop 2) | `Step-CountsGuard`: while Teeth runs, the Doctor copies LiveCounts (IN, OUT, FrozenSeconds, ShiftKey) to `C:\ProgramData\Teeth\counts-latest.txt` (outside the Teeth folder) and appends changes to `App Files\Logs\counts-history.txt`. When Teeth is NOT running, the database shows 0/0 and the saved ShiftKey equals the current shift key, it writes the saved counts back BEFORE Teeth is started. Never restores a previous shift. Any error = does nothing. Known limit: Reset Result followed by a Teeth restart inside the 5-minute window can bring the old counts back. |

The number on laptop 1 ("IN sent since start: N") is a running total since TeethSender started; it returns to 0 when TeethSender or laptop 1 restarts, also when the Doctor restarts it. The real totals per shift are Result IN / OUT on laptop 2 (saved every 3 seconds, reloaded at start; zeroed only by a shift change, Reset Result, or a different folder with a new database) and `Log Data\Teeth_Log.xlsx`.

Always run TEETH-AUTO.bat from `C:\Teeth`; another copy of the folder has its own empty `Teeth.sqlite`.

Limits of the counts safety net (also in the PDF update pages): (1) Reset Result followed by a Teeth restart inside the 5-minute window can bring the old counts back. (2) If Teeth starts with a brand-new empty database (for example from another folder), the counts are NOT restored; the last numbers are in `counts-history.txt` and can be entered with Settings > Admin > correction. `TEETH-UNDO.bat` does not undo the .NET install, the time zone change or `C:\ProgramData\Teeth`.

How to run and test Doctor 2: replace `App Files\Tools\Teeth-Doctor.ps1` on both laptops (or unzip the new project zip into `C:\Teeth`, keeping `Teeth.sqlite`, `Log Data`, `link.ini`, `sender.ini`, `pending.txt`); run `App Files\Tools\Run-Doctor-SelfTest.bat` (must end with ALL CHECKS PASSED: static 35 + logic 42); `TEETH-CHECK-ONLY.bat`; then `TEETH-AUTO.bat` from `C:\Teeth`; then tests D1-D15 (D10-D15 are new). Developer tests here: `App Files\Source_Build\doctor-tests` (`Test-Doctor-Static.ps1`, `Test-Doctor-Logic.ps1`, `Test-Doctor-Scenarios.ps1` + `Simulate-Doctor.ps1`; result file `RESULT-2026-11-12.txt`: 35 + 42 + 36 = 113 checks passed).
