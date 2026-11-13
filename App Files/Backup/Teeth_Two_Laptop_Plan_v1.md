# Teeth: two-laptop plan

Result IN comes from laptop 1 over an Ethernet cable. Result OUT comes from the scanner on laptop 2. Everything stays offline.

## 1. Goal

| | Laptop 1 | Laptop 2 |
|---|---|---|
| IP address | `192.168.50.1` | `192.168.50.2` |
| Hardware | Key button (sends P + Enter), printer | Barcode scanner, printer, monitor |
| Software | Print software + **TeethSender** (new) | Print software + **Teeth** (updated) |
| Role | Result **IN** | Result **OUT** + dashboard |

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
| Cable between the two laptops | Done |
| Static IPs set, ping works both ways | Done |
| Teeth listener, status light, scan detection | Written, compiles |
| TeethSender (laptop 1) | Written, compiles |
| Cable messaging logic (counting, retry, queue, restart) | Tested with a fake sender and listener |
| Keyboard hooks (key button, scanner) | **Not tested on real hardware yet** |
| `Teeth.exe`, `TeethSender.exe` | Rebuilt and included (Mono compiler, **not yet run on Windows**) |
| `HOW-TO-USE.pdf` | Rewritten for the two laptops, 7 pages with pictures |

## 4. Steps to do

### Step 1: Copy Teeth to laptop 2
- [ ] Copy the whole `Teeth` folder to the Desktop or `C:\Teeth` (its `Teeth.exe` is already the new version)
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
- [ ] Copy the whole `Teeth_Sender` folder to `C:\TeethSender` on laptop 1 (`TeethSender.exe` is already built)
- [ ] Run `Test-Link.bat` and confirm **RESULT: OK**
- [ ] Double-click `TeethSender.exe`. A bar appears at the bottom-right of the screen
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

## 6. Settings

| File | Key | Default | Meaning |
|---|---|---|---|
| `Teeth\App Files\link.ini` | `Port` | `5055` | Port Teeth listens on |
| | `LinkTimeoutSeconds` | `6` | Silence before the screen shows NOT connected |
| | `ScanEnabled` | `true` | Count scans as OUT |
| | `ScanMinChars` | `4` | Minimum characters before Enter to count as a scan |
| | `ScanMaxGapMs` | `50` | Maximum ms between keys of a scan |
| `TeethSender\sender.ini` | `Host` | `192.168.50.2` | IP of laptop 2 |
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
| Both red, Sender bar red | Cable plugged in on both laptops? Link lights on the ports? |
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
- Only one install per laptop. The two Teeth databases are never shared.

## 10. Open questions

1. Does the print software on laptop 2 print straight from the scan, or does it need a separate button?
2. Should OUT count the scan itself, or only after a label is really printed? (Counting real print jobs is more accurate but needs the Windows print log enabled by an administrator.)
3. Should the Excel log record the scan or print detail (for example the code) as well as the counts?
4. Should a supervisor be able to subtract a wrong count (for example a Sensor Correction field for IN and OUT)?

## 11. Files in the package

```
Teeth_Project_2Laptops.zip
  SETUP-TWO-LAPTOPS.txt
  docs/
    Teeth_Two_Laptop_Plan.md        (this file)
    images/                         (the 4 pictures used in the guide)
  Teeth/
    Teeth.exe                       (new version, already rebuilt)
    HOW-TO-USE.pdf                  (rewritten, 7 pages with pictures)
    App Files/
      README.md                     (updated: Step 6)
      link.ini                      (created on first run)
      Backup/                       (previous Teeth.exe and guide)
      Tools/Allow-Laptop1-Firewall.bat
      Source/Teeth/
        LinkSettings.cs   LinkServer.cs   ScanDetector.cs   (new)
        MainDashboard.cs                                   (updated)
  Teeth_Sender/
    TeethSender.exe                 (ready to copy to laptop 1)
    SENDER-HOW-TO.txt
    Test-Link.bat
    Source/TeethSender.sln
    Source/TeethSender/
      Program.cs   LinkClient.cs   PrintKeyDetector.cs   SenderSettings.cs
```
