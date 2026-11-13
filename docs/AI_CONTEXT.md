# AI_CONTEXT: Teeth two-laptop project (final version 2, 2026-11-07; package 3 with the one-click Teeth Doctor, 2026-11-11; Doctor 2, 2026-11-12)

Read this first if you are an AI assistant helping with this project. It is a compact, accurate summary. Do not claim anything is tested on Windows unless the owner reports it.

## 1. What the project is
"Teeth" is an offline shop-floor production dashboard (Windows Forms, C#, .NET Framework 4.8, Teeth is x64). It shows Plan / Target / Result / Difference for IN and OUT, switches Day (08:00-20:00) and Night (20:00-08:00) shift automatically, and logs each shift to `Log Data\Teeth_Log.xlsx`. State is in `App Files\Teeth.sqlite`.

This version splits the work over **two laptops** joined by **one Ethernet cable**, with **no internet, router or server**.

| | Laptop 1 | Laptop 2 |
|---|---|---|
| Role | Result IN | Result OUT + dashboard |
| Teeth link address | 192.168.50.1 (USB-Ethernet adapter) | 192.168.50.2 (USB-Ethernet adapter) |
| Hardware | Key button (sends `P` then `Enter`), printer | Barcode scanner, printer, monitor |
| Programs | Print software + **TeethSender** | Print software + **Teeth** |

Both laptops run print software that generates label information for printers and already use their built-in Ethernet port. **Decision:** the Teeth link runs on a separate USB-to-Ethernet adapter on each laptop so the built-in port (print network) is never changed. No code change was needed for this.

## 2. How it works (protocol)
- TCP, port **5055**, plain text lines. Teeth listens on `IPAddress.Any`; TeethSender connects to `Host` from `sender.ini` (default `192.168.50.2`).
- IN: laptop 1 detects `P` then `Enter` within `KeyWindowMs` (500) via a system-wide keyboard hook on its own thread (keys are never blocked, so the print software still receives them), sends `IN <id>`; Teeth counts it once and replies `OK <id>`. A repeated id is answered but not counted again (last 5000 ids kept in memory only).
- Unconfirmed IN counts wait in `pending.txt` on laptop 1 and are resent (survives restart of laptop 1 or Teeth closed).
- Heartbeat: `PING` every 2 s, `PONG` back. Teeth shows **Laptop 1: Connected** (green) or **NOT connected** (red after `LinkTimeoutSeconds`, default 6) or **Link error: port 5055 busy**. The Sender bar (bottom-right, always on top, no focus) shows `Teeth link: OK (IN sent since start: n)`, `NOT CONNECTED (waiting: n)` or `Key hook NOT running`.
- OUT: Teeth (laptop 2) uses its own keyboard hook; a scan = at least `ScanMinChars` (4) characters each within `ScanMaxGapMs` (50) of the previous, ended by `Enter`. Enter and Space typed alone count nothing. Fallbacks in the Teeth window: `Ctrl+Alt+I` = +1 IN, `Ctrl+Alt+O` = +1 OUT, `Ctrl+Alt+S` = Change Schedule.
- Shift, target and log follow the **laptop 2 clock**. Offline laptops need the date, time and time zone set by hand and checked weekly.

## 3. Files (all programs are in the MAIN folder of the zip)
```
Teeth_Project_2Laptops_FINAL.zip
  Teeth.exe                 laptop 2 (run only here)
  TeethSender.exe           laptop 1 (run only here)
  TEETH-AUTO.bat            BOTH laptops: one click = automatic setup (first time) / check and repair (later)   [NEW, section 6g, untested on Windows]
  TEETH-CHECK-ONLY.bat      BOTH laptops: report only, changes nothing     [NEW]
  TEETH-UNDO.bat            BOTH laptops: removes the automatic setup      [NEW]
  Test-Link.bat             laptop 1 link test
  START-HERE_Teeth_Quick_Visual_Guide.pdf   short guide (10 landscape pages, pictures and flows): the one to follow
  Teeth_Troubleshooting_Guide.pdf   13 problems (ping, port test, port busy, exe won't start, key button, scanner, link drops, printing, counts, Unhandled exception/Method not found, Test-Link on laptop 2, wrong program/order, cable unplugged/moved) + connection-loss page + FAQ + case log + undo: flow picture, copy-paste commands, what to send
  Teeth_Rollout_Kit.pdf     using it on more laptop pairs: pre-flight check, per-pair 24-step checklist, record sheet
  SETUP-TWO-LAPTOPS.txt  SENDER-HOW-TO.txt
  Log Data/                 Teeth_Log.xlsx
  App Files/                lib/ (dlls), README.md, Backup/ (old exe, v1 guide + plan), Tools/
                            (Teeth-Doctor.ps1, Teeth-Doctor-Hidden.vbs [NEW], Allow-Laptop1-Firewall.bat, Check-Requirements.bat, Update-App.bat, Test-Link.ps1, ...),
                            Logs/ (created by the Doctor: Teeth-Doctor-log.txt, LAST-STATUS.txt), teeth-doctor.ini (created by the Doctor),
                            Source/ (Teeth.sln; new: LinkSettings.cs LinkServer.cs ScanDetector.cs; updated MainDashboard.cs),
                            Source_Sender/ (TeethSender.sln; Program.cs LinkClient.cs PrintKeyDetector.cs SenderSettings.cs)
                            Teeth.sqlite + link.ini are created on first run
  docs/                     Teeth_Full_Reference_Guide.pdf (17 pages, every detail), AI_CONTEXT.md,
                            Teeth_Two_Laptop_Plan.md, images/
```
The whole folder is copied to `C:\Teeth` on BOTH laptops. `Teeth.exe` finds `App Files` and `Log Data` next to itself; `TeethSender.exe` creates `sender.ini` and `pending.txt` next to itself. Never run `Teeth.exe` on laptop 1.

## 4. Settings
| File | Key | Default |
|---|---|---|
| `App Files\link.ini` | `Port` 5055, `LinkTimeoutSeconds` 6, `ScanEnabled` true, `ScanMinChars` 4, `ScanMaxGapMs` 50 | created on first run |
| `sender.ini` (next to TeethSender.exe) | `Host` 192.168.50.2, `Port` 5055, `KeyWindowMs` 500 | created on first run |

Changing `Port` also needs `PORT` in `Allow-Laptop1-Firewall.bat` (run again as administrator). Changing the address range also needs `Host` in `sender.ini` and `HOST` in `Test-Link.bat`.

## 5. Setup order: 24 steps in 2 phases (quick guide maps on pages 2-3; reference guide has every detail)
**Phase 1: prove the network without our software (steps 1-15).**
1. BOTH: restore points: `ipconfig /all > "%USERPROFILE%\network-before-teeth.txt"`; laptop 2 (admin cmd) `netsh advfirewall export "%USERPROFILE%\firewall-before-teeth.wfw"`; print a test label.
2. BOTH: set date/time/time zone by hand (laptop 2 clock drives shifts).
3. BOTH: USB adapter in each laptop (same USB port always), cable between the adapters.
4. LAPTOP 1: USB adapter IP 192.168.50.1 / 255.255.255.0, empty gateway and DNS (check the print network does not use 192.168.50.x).
5. LAPTOP 2: USB adapter IP 192.168.50.2 / 255.255.255.0.
6. BOTH: `ipconfig` shows the address on the USB adapter, built-in port unchanged. LAPTOP 1 `ping 192.168.50.2`: no reply is normal (ICMP blocked by default).
7. LAPTOP 2: `netstat -ano | findstr :5055` prints nothing.
8. LAPTOP 2 (admin cmd): `netsh advfirewall firewall add rule name="Teeth link" dir=in action=allow protocol=TCP localport=5055 profile=any remoteip=192.168.50.0/24` and `netsh advfirewall firewall add rule name="Teeth ping" dir=in action=allow protocol=icmpv4:8,any profile=any remoteip=192.168.50.0/24` (each answers Ok.). Same effect: `Allow-Laptop1-Firewall.bat`. Undo: `Remove-Teeth-Firewall-Rules.bat`.
9. LAPTOP 1: ping now replies.
10. LAPTOP 2 (PowerShell): stand-in listener: `$l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 5055); $l.Start(); Write-Host 'Listening on 5055. Press Ctrl+C to stop.'; try { while ($true) { if ($l.Pending()) { $c = $l.AcceptTcpClient(); Write-Host ('Connection from ' + $c.Client.RemoteEndPoint); $c.Close() }; Start-Sleep -Milliseconds 200 } } finally { $l.Stop() }` (or `App Files\Tools\Test-Port-Listener.bat`).
11. LAPTOP 1 (PowerShell): `Test-NetConnection 192.168.50.2 -Port 5055` -> `TcpTestSucceeded : True`, `InterfaceAlias` = the USB adapter.
12. LAPTOP 2: Ctrl+C to stop the listener (else Teeth shows "port busy").
13. LAPTOP 1: Notepad, press the key button: `p` + new line.
14. LAPTOP 2: Notepad, scan an item: whole code + new line.
15. BOTH: print one label each: printing unchanged.

**Phase 2: install and test our programs (steps 16-24).**
16. BOTH: unblock the ZIP, copy the whole folder to `C:\Teeth`.
17. BOTH: `App Files\Tools\Check-Requirements.bat` (64-bit Windows + .NET Framework 4.8).
18. LAPTOP 2: start `Teeth.exe`, leave it open (red NOT connected is normal).
19. LAPTOP 2: `Test-Link.bat` -> "THIS IS LAPTOP 2 ... RESULT: OK - Teeth is listening".
20. LAPTOP 1: `Test-Link.bat` (only after step 19) -> "RESULT: OK".
21. LAPTOP 1: start `TeethSender.exe` (bar green, Teeth shows Connected); tray icon > Show last keys lists `P` then `Enter`; optional shortcut in `shell:startup`.
22. LAPTOP 1: press the key button -> IN +1 on laptop 2.
23. LAPTOP 2: scan an item -> OUT +1.
24. BOTH: print one label each; then run the 12 acceptance tests in `Teeth_Two_Laptop_Plan.md`.

## 6. Status (be honest about it)
- **Proven on the owner's real laptops (2026-11-07), Phase 1:** USB adapters and IPs, ping after the firewall rules, TCP 5055 across the cable (stand-in listener, `TcpTestSucceeded : True`), key button types `p` + new line, scanner types code + new line, printing unchanged.
- Tested only with a fake sender and listener: cable messaging logic (counting, retry, queue, restart).
- **Phase 2 first run (step 18), both laptops, build 1:** `Teeth.exe` started and showed the dashboard, but an "Unhandled exception" window appeared: `Method not found: 'System.String[] System.String.Split(Char, System.StringSplitOptions)'`; Plan/Target stayed empty and the link line showed "Laptop 1: ...". Check-Requirements passed on both laptops (step 17).
- **Cause:** build 1 was compiled against Mono's own newer class library, so `value.Split(':')` in `ShiftLogic.cs` bound to `Split(char, StringSplitOptions)`, which does not exist in .NET Framework 4.8. Confirmed by disassembling both exes (`ikdasm`): build 1 calls `Split(char, ...)`, build 2 calls `Split(char[])`.
- **Fix (Build 2, 2026-11-08):** source `ShiftLogic.cs` now uses `Split(new[] { ':' })`, and both exes were rebuilt against the real .NET Framework 4.8 reference assemblies (`/usr/lib/mono/4.8-api`), so any API missing in .NET Framework 4.8 is now a compile error. `TeethSender.exe` had no such call. Build recipe: `App Files\Source_Build\build-with-mono.sh`; `VERSION.txt` in the main folder names the build; old exes are in `App Files\Backup`.
- **Build 2 confirmed by the owner (2026-11-08): "it works"** on both laptops after following steps 16-24 (results of each individual test were not reported).
- **Not separately confirmed:** the startup shortcut, the full list of 12 acceptance tests (shift change at 08:00/20:00, cable-outage and restart cases), and long-run behaviour.

## 6b. Observed environment (owner's laptop pair 1)
- Laptop 1: USB adapter "Ethernet 4" = 192.168.50.1/24; built-in Ethernet 192.168.0.97/22 (gateway 192.168.0.1, print network); Wi-Fi 10.98.101.89/22.
- Laptop 2: USB adapter "Ethernet 2" = 192.168.50.2/24; built-in Ethernet 192.168.3.73/22 (gateway 192.168.0.1). Windows Firewall ON for Domain, Private and Public. Port 5055 was free.
- The print network (192.168.0.0/22) does not overlap 192.168.50.x. The built-in Ethernet has a company DNS suffix, so IT policy could override hand-made firewall rules.
- Lesson: ping from laptop 1 to laptop 2 timed out until the `Teeth ping` rule was added (ICMP blocked by default), although ARP already listed laptop 2: a ping timeout alone does not mean the cable is bad. The port test decides.

## 6c. Build notes
Supplied exes: built on Linux with Mono 6.8 `mcs`, `-nostdlib` against `/usr/lib/mono/4.8-api` (+ Facades), Teeth `-platform:x64` with `-win32icon`, Sender `-platform:anycpu`. `mcs` does not support C# 7.2 `readonly struct`, so the build script strips `readonly` from `ShiftLogic.cs` in a temporary copy only; the delivered source keeps it (Visual Studio builds it as net48).

## 6e. Case log (what actually happened on pair 1)
1. First Test-Link run: laptop 1 ping 100% loss + NOT OK; laptop 2 (run there by mistake) ping reply + port closed. Causes: Windows blocks inbound ping by default (no ping rule); Teeth was not running; on laptop 2 the ping went to itself. Fix: the two firewall rules (step 8), open Teeth first, Test-Link on laptop 2 then laptop 1; the new Test-Link detects which laptop it is and says so.
2. ipconfig on both laptops was correct (USB adapters "Ethernet 4" = 192.168.50.1 and "Ethernet 2" = 192.168.50.2, no gateway); laptop 2 could ping laptop 1 but not the reverse; `arp -a` on laptop 1 already listed laptop 2's physical address, so the cable worked and only inbound ping on laptop 2 was blocked.
3. Owner asked whether the firewall commands affect the existing Ethernet/print software: no (incoming allow rules only). The rules were tightened to `remoteip=192.168.50.0/24`; restore points and `Remove-Teeth-Firewall-Rules.bat` added.
4. Phase 1 proof without our software passed (listener + `Test-NetConnection` True via the USB adapter, key button `p`+newline, scanner code+newline, printing).
5. Step 18 on both laptops with build 1: `Unhandled exception ... Method not found: String.Split(Char, StringSplitOptions)`; Plan/Target empty. Cause: wrong class library at build time. Fix: Build 2 (2026-11-08). Owner confirmed it works.
6. Navigation problem: the first setup map did not say which laptop does each step. Fix: 24-step maps (Phase 1 without software, Phase 2 with our programs), every step labelled LAPTOP 1/LAPTOP 2/BOTH.

## 6f. Link failure behaviour (verified by simulation, 2026-11-09)
`App Files\Source_Build\link-simulation\` runs the REAL `LinkClient.cs` and `LinkServer.cs` against a fake cable (pull / plug back / swallow the OK reply). Result: **13 of 13 checks passed** (`RESULT-2026-11-09.txt`):
- 10 key presses: all delivered, link green.
- Cable pulled 12 s, 25 key presses meanwhile: Teeth link turns red (within 6 s), the sender notices, the 25 counts wait and are saved in `pending.txt`; after plug-in the link is green again after about 1.1 s and IN rises by exactly 25, each once.
- OK reply lost on purpose: the retry reuses the id, Teeth answers OK without counting twice (no double count).
- Sender process restarted with 3 counts waiting: `pending.txt` keeps them; the new process sends them.
- Teeth closed while keys are pressed: counts wait and arrive when Teeth opens.
Mechanism: every press gets a GUID, is queued and written to `pending.txt`, sent as `IN <id>`, removed only after `OK <id>`; `PING` every 2 s (`PONG` back); the sender reconnects every ~2 s; Teeth keeps the last 5000 ids and shows red after `LinkTimeoutSeconds` (6).
**What is NOT protected (by design):** IN presses while `TeethSender.exe` is not running (the key hook lives in the sender); OUT scans while `Teeth.exe` is closed (the scan hook lives in Teeth); a counted IN whose OK was lost, if Teeth restarts before the retry (id memory is in RAM only: very rare double count); outages across 08:00/20:00 count in the new shift. Moving a USB adapter to another USB port can create a new adapter without `192.168.50.x`: set the IP again. Manual fallback in the Teeth window: `Ctrl+Alt+I` (+1 IN), `Ctrl+Alt+O` (+1 OUT).
The simulation cannot test the Windows keyboard hooks, the real USB adapters or Windows' own link-up delay (a few seconds).
UI: `Teeth.exe` (laptop 2 big screen) and `TeethSender.exe` (laptop 1 small status bar + tray menu with "Show last keys (test)" and "Exit") have windows; `Test-Link.bat` prints a check; the one-time setup used to be manual by the 24 steps. Since package 3 there is `TEETH-AUTO.bat` (section 6g) that does these steps and the repairs automatically; it is NOT yet tested on a real Windows laptop, so the 24 manual steps remain the proven fallback.

## 6f2. The number on the Sender bar (Build 2b, 2026-11-12)
About the number on laptop 1: the bar shows "Teeth link: OK (IN sent since start: N)". N is only a running total of the key presses that Teeth has confirmed since TeethSender was started. It does NOT reset at midnight or at a shift change, it goes back to 0 whenever TeethSender or laptop 1 is restarted (the automatic tool restarting TeethSender does this too), and it is NOT the shift or day total. The correct totals per shift (Day 08:00-20:00, Night 20:00-08:00, by the laptop 2 clock) are Result IN and Result OUT on the Teeth screen of laptop 2 and in Log Data\Teeth_Log.xlsx. The two numbers are expected to be different. Code: `LinkClient.Delivered` is an in-memory `long` counted since the process started, never persisted and never reset by date; Teeth keeps the real totals per shift key (`ShiftKeys`, shift start 08:00 / 20:00, laptop 2 clock). Build 2b changed only the label text in `Source_Sender\TeethSender\Program.cs` (width unchanged, 330 px; the longest text, `NOT CONNECTED (waiting: n)`, was already wider). A rebuild of the unchanged Build 2 sources with `build-with-mono.sh` was byte-identical to the shipped Build 2 exes, so the toolchain is reproducible; the Build 2 sender is in `App Files\Backup\TeethSender_Build2.exe`. Option NOT built: making the sender show the shift total (needs a protocol change and tests on the laptops).

## 6g. Teeth Doctor: one-click setup and automatic repair (package 3, 2026-11-11)
**Status: written and logic-tested offline only. NOT yet run on a real Windows laptop. Do not claim it works until the owner reports Doctor acceptance tests D1-D15 (D10-D15 are the Doctor 2 tests) (in `Teeth_Two_Laptop_Plan.md`).** `Teeth.exe` is unchanged (Build 2); `TeethSender.exe` is Build 2b (only the bar wording changed, section 6f2).

Why: the owner did not want to type cmd/PowerShell commands to set up and test, and wanted the connection problems repaired without touching the laptops.

Files (main folder): `TEETH-AUTO.bat` (self-elevates, runs `Teeth-Doctor.ps1 -Mode Auto`), `TEETH-CHECK-ONLY.bat` (`-Mode Check`, changes nothing, no admin), `TEETH-UNDO.bat` (`-Mode Undo`, asks first). In `App Files\Tools`: `Teeth-Doctor.ps1` (the program) and `Teeth-Doctor-Hidden.vbs` (starts it with no window for the scheduled task). Created at run time: `App Files\teeth-doctor.ini` (Role, AdapterMac, Laptop1Ip, Laptop2Ip, PrefixLength, RestartPrograms, WatchEveryMinutes), `App Files\Logs\Teeth-Doctor-log.txt` and `App Files\Logs\LAST-STATUS.txt`. Tests (all in `App Files\Source_Build\doctor-tests`, 90 checks, all passed on PowerShell 7.4 on Linux): `Test-Doctor-Logic.ps1` (32: ini, MAC, adapter safety and choice, powercfg parsing, overall colour; each safety rule was mutation-checked), `Test-Doctor-Scenarios.ps1` (27: runs the real script through `Simulate-Doctor.ps1`, which replaces every Windows cmdlet that changes the PC by a logging function: fresh laptop 1/2, Setup then Fix changes nothing the second time, healthy PC, Silent prints nothing, Check changes nothing, leftover listener, wrong firewall rule, print network on 192.168.50.x, USB adapter with gateway, no adapter, cable out, Undo), `Test-Doctor-Static.ps1` (35: ASCII, CRLF, parser errors, no PowerShell-7-only syntax or parameters, Join-Path with 2 arguments, no assignment to automatic variables, called functions exist, command names known, bat/vbs point to existing files and valid modes, the text documents tell the same procedure). `App Files\Tools\Run-Doctor-SelfTest.bat` runs the static and logic checks on the owner's PC with its own Windows PowerShell 5.1 (reads only). The simulator mocks the cmdlets with signatures written from memory, not from the Windows modules, so it proves the control flow, not the real cmdlet behaviour. The static check found and fixed one real defect (assignment to the automatic variable `$args`).

Procedure told to the owner (identical in every document; keep it identical): 1 unzip both zips (outer, then `Teeth_Project_2Laptops_FINAL.zip`); 2 copy the folder to both laptops; 3 plug one USB adapter into each laptop and the cable between them (the owner does NOT set the IP by hand, the Doctor sets it); 4 laptop 2 first, then laptop 1: `Run-Doctor-SelfTest.bat` (must say ALL CHECKS PASSED); 5 laptop 2 first, then laptop 1: `TEETH-AUTO.bat`, Yes, type 2 or 1 (answer `y` if it asks to update an old `C:\Teeth`); 6 read GREEN/YELLOW/RED (laptop 1 can say RED 'no contact' if laptop 2 was not ready: click again); 7 run Doctor tests D1-D15 (D1/D2 only check what step 5 did); 8 every day the programs start by themselves after log-on, routine = Quick Guide page 15 'Every day: the screen and the routine' (page 15 of the Quick Guide); afterwards use `C:\Teeth`. Laptop 1 = key button / IN / TeethSender.exe; laptop 2 = scanner and screen / OUT / Teeth.exe.

Modes: `Auto` = Setup if no Role saved or no scheduled task, else Fix. `Setup` = Fix plus: asks "1 or 2" (with a hint), copies the folder to `C:\Teeth` (robocopy; never overwrites `Teeth.sqlite`, `link.ini`, `sender.ini`, `pending.txt`, `Log Data`; asks before updating an existing `C:\Teeth`), saves restore points (`network-before-teeth.txt`, laptop 2 `firewall-before-teeth.wfw`), unblocks the files, turns off adapter energy-saving driver settings, creates the task. `Fix` = checks and repairs (this is what the task runs, `-Silent`). `Check` = report only. `Undo` = removes the task, both firewall rules, sets the Teeth USB adapter back to DHCP, restores USB selective suspend to the Windows default; keeps the programs, database, Log Data and the ini files of the programs.

What it checks and repairs, bottom-up: 64-bit Windows, .NET 4.8, program files, folder writable; USB adapter found (USB only, never the built-in port, never Wi-Fi, never an adapter with a gateway or an address from another network; remembered by MAC so a move to another USB port is recognised); adapter enabled; IP 192.168.50.1 (laptop 1) / .2 (laptop 2), mask /24, no gateway, no DNS; refuses if another network on the PC already uses 192.168.50.x; USB adapter power saving (WMI `MSPower_DeviceEnable`) and USB selective suspend (powercfg) turned off; laptop 2: firewall rules `Teeth link` (TCP, port from `link.ini`) and `Teeth ping` (ICMPv4 echo), both inbound and only from 192.168.50.0/24, recreated when missing or wrong; laptop 2: a leftover PowerShell test listener on the port is stopped, any other program on the port is only reported; `Teeth.exe` (laptop 2) / `TeethSender.exe` (laptop 1) running, started again like a double-click (via explorer.exe, not as administrator) when closed and `RestartPrograms` is not `false`; wrong program on the wrong laptop and duplicate copies are only reported; laptop 1: link to laptop 2 (an established connection counts as OK, else a port probe with retries; the failure text says whether it is the cable signal, a missing Teeth on laptop 2, or no contact); laptop 2: Teeth listening and whether laptop 1 is connected; laptop 1: number of counts waiting in `pending.txt`; sleep timeout warning (never changed); clock shown; scheduled task `Teeth Doctor` (at log-on and every 5 minutes, highest privileges, user = the logged-on user, hidden via the .vbs, one run at a time via a mutex).
Result: GREEN / YELLOW (notes) / RED (problems it cannot fix, each with a plain instruction). Statuses in the log: OK, FIXED, WARN, FAIL, INFO.
The Doctor cannot repair itself and sends nothing anywhere: when it fails the owner sends `LAST-STATUS.txt` / `Teeth-Doctor-log.txt` and uses the manual steps meanwhile. Doctor 2 (2026-11-12) additions: installs .NET 4.8 when missing (interactive Setup/Fix only; installer from the Teeth folder, drive roots or download via go.microsoft.com fwlink 2088631; runs only if Authenticode signature is valid and signed by Microsoft; /q /norestart; exit 3010 = restart needed; 5100 = Windows too old), one Restart-NetAdapter try when Laptop 1 adapter is Disconnected (interactive only), time zone check against ini key TimeZone (default SE Asia Standard Time; interactive Y changes it and keeps the wall-clock time; silent run only warns). Counts safety net (Laptop 2, Step-CountsGuard): copies LiveCounts to ProgramData\Teeth\counts-latest.txt while Teeth runs (history in Logs\counts-history.txt); restores only when Teeth is NOT running, DB is 0/0, and the saved ShiftKey equals the current shift key. Reads/writes Teeth.sqlite through lib\System.Data.SQLite.dll; any error = does nothing. Known limit: an operator Manual Reset followed by a Teeth restart within 5 minutes (before the next Doctor run) can bring the old counts back. Not yet tested on a real Windows PC. The owner still does by hand: unzip both zips, plug adapters and cable, click Yes, type 1/2 once, click Run anyway on SmartScreen/antivirus warnings.
Cannot fix by design: unplugged/broken cable or adapter, missing adapter, wrong clock, IT policy blocking firewall or scheduled tasks, antivirus blocking the exes, IN presses while `TeethSender.exe` was closed (the Doctor only shortens that time to at most the check interval).
Known risks to watch in the first real test: the explorer.exe de-elevation trick for starting the programs (fallback: a normal elevated start), the `-RepetitionDuration` of the task trigger on the owner's Windows build, the WMI power-saving property on the owner's adapter, adapter names/`PnPDeviceID` of the owner's USB adapters (expected to start with `USB\`), IT policy on the company laptops.

## 6d. Using it on more laptop pairs
- Supported by design: each pair is an independent system (own cable, own `Teeth.sqlite`, `Teeth_Log.xlsx`, firewall rules, line name and schedule via Change Schedule; default line is `Line A`). The same addresses 192.168.50.1/.2 can be reused on every pair because each cable is separate. Same zip, same 24 steps. **Only pair 1 has been proven**; new pairs must pass the pre-flight check in `Teeth_Rollout_Kit.pdf` (Windows 10/11 64-bit + .NET 4.8, two USB-Ethernet adapters, admin rights on laptop 2, print network not using 192.168.50.x, key button sends P+Enter, scanner types code+Enter, antivirus/IT allows the exes and rules).
- Not supported: more than one sender per Teeth (all IN counts go to one number; the server accepts several clients but does not separate them), one screen for several lines, Windows older than 10 or 32-bit, joining pairs in one switch/hub, Wi-Fi as the link.
- Different key-button or scanner hardware may need detector changes (`PrintKeyDetector.cs`, `ScanDetector.cs`) or settings (`KeyWindowMs`, `ScanMaxGapMs`, `ScanMinChars`).

## 7. Known limits
Counts held during an outage are counted in the shift running when they arrive. After a Teeth restart a counted-but-unacknowledged IN could be counted twice (very rare). Scan detection assumes the scanner types like a keyboard (code, then Enter). One install per laptop; the two databases are never shared.

## 8. Open questions for the owner
1. Does print software on laptop 2 print straight from the scan or via a separate button?
2. Count the scan as OUT, or only after a label really prints (needs Windows print log enabled by an administrator)?
3. Log the scanned code in the Excel log?
4. Supervisor subtraction (Sensor Correction field for IN and OUT)?
5. Does the print network use 192.168.50.x? How is each printer connected (USB, Ethernet to the printer, company network)?

## 9. Build notes
Teeth: `App Files\Source\Teeth.sln`, Release + x64, Rebuild, close Teeth.exe, run `App Files\Tools\Update-App.bat` (never touches the database or Log Data). Sender: `App Files\Source_Sender\TeethSender.sln`, Release, Rebuild, copy the new `TeethSender.exe` from `bin\Release\net48` over the old one on laptop 1. Admin PIN (Sensor Correction) is the constant `AdminPin` in `AdminPinForm.cs`. C# LangVersion 7.3.

## 10. Guidance for AI assistants
- Answer from this file and the zip; do not invent features, messages or file names.
- When troubleshooting, work bottom-up: cable and lights, `ipconfig`, `ping`, port test, green bar, key and scan tests.
- First ask the owner to double-click `TEETH-AUTO.bat` (or `TEETH-CHECK-ONLY.bat`) on the laptop in question and to send `App Files\Logs\LAST-STATUS.txt` (and `Teeth-Doctor-log.txt` if asked). Its [PROBLEM] / [NOTE] lines follow the same bottom-up order and say which laptop to look at. The Doctor is untested on Windows: if it misbehaves, fall back to the manual steps and the troubleshooting guide, and do not claim a Doctor fix works before the owner confirms it.
- Never advise changing the built-in Ethernet port settings of either laptop.
- `Method not found` in an exe means it was built against the wrong reference assemblies: rebuild against `/usr/lib/mono/4.8-api` (see `build-with-mono.sh`), do not tell the owner to change Windows settings.
- `Test-Link.bat` (calls `App Files\Tools\Test-Link.ps1`) detects the laptop by its IP: on laptop 1 it tests ping + port 5055 across the cable; on laptop 2 it only checks that Teeth is listening locally (a ping reply there comes from itself and proves nothing about the cable). The firewall script opens TCP 5055 and also allows ping, both only from the cable network 192.168.50.0/24 (variable REMOTE in the .bat); Windows blocks ping by default on a new Public network.
- Use `Teeth_Troubleshooting_Guide.pdf` problem numbers 1-13 when guiding the owner; each row is CHECK (command) / GOOD / BAD = fix.
- Ask for the exact on-screen text, the step or test number, and the "Show last keys (test)" result.
- Keep answers short and in simple words; the owner is not a developer.

## Documents (version of 2026-11-12, Doctor 2)
The four PDFs were rebuilt for this version: their front chapters describe TEETH-AUTO.bat as it is now (the Quick Visual Guide has a 7-page one-click chapter with two flowcharts: the workflow and what to do when it is not green); the manual numbered pages are unchanged and still valid. `Teeth_Two_Laptop_Plan.md` has the section on Doctor 2 and the tests D1-D15. Open question for the owner: which number dropped to 0 - the Laptop 1 "IN sent since start" bar (expected, resets when TeethSender restarts) or Result IN/OUT on laptop 2?

Limits of Step-CountsGuard: it does NOT restore when Teeth starts with a brand-new empty database (the Doctor cannot read a missing database); it can bring old counts back if Reset Result is followed by a Teeth restart inside the 5-minute window. Test results: App Files\Source_Build\doctor-tests\RESULT-2026-11-12.txt (35 static + 42 logic + 36 scenario = 113, simulator only). How to run: Run-Doctor-SelfTest.bat, TEETH-CHECK-ONLY.bat, TEETH-AUTO.bat from C:\Teeth, then tests D1-D15.
