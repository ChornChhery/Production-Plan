# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Planned
- Versioned releases on the Releases page
- Daily summary sheet in the Excel log (all shifts of one day)

## [1.0.0] - 2026-11-14

First stable release. In use on real laptops; the acceptance tests D1 to D15 passed.

### Added
- GitHub Actions: builds both programs and runs the Doctor test suites on every push
- Real screenshots and a short screen recording
- Counts are restored even when Teeth starts with a new empty database in the same shift
- The sender's running total is kept across restarts of TeethSender

### Changed
- The admin PIN is configurable instead of a built-in value

## [0.4.0] - 2026-11-12 - Doctor 2

### Added
- `TEETH-AUTO.bat` installs .NET Framework 4.8 when it is missing (asks once, checks the Microsoft signature)
- Time zone check against `TimeZone=` in `teeth-doctor.ini`; changes it after a *Yes* and keeps the clock time
- One switch off and on of the USB adapter when it reports *Disconnected*
- Counts safety net on laptop 2: backup of IN/OUT outside the Teeth folder, restored in the same shift if the database shows 0 / 0
- Doctor acceptance tests D10 to D15 and 113 automatic checks (35 static, 42 logic, 36 scenarios)
- Quick Visual Guide: workflow flowchart and troubleshooting flowchart

## [0.3.0] - 2026-11-11 - Teeth Doctor

### Added
- One-click setup and self-repair: `TEETH-AUTO.bat`, `TEETH-CHECK-ONLY.bat`, `TEETH-UNDO.bat`
- Scheduled task *Teeth Doctor* (at log-on and every 5 minutes, hidden)
- Restore points for network and firewall, plain-language result file `LAST-STATUS.txt`

## [0.2.0] - 2026-11-08 - Build 2

### Fixed
- `Teeth.exe` showed an error window at start (*Method not found: String.Split*) on both laptops

## [0.1.0] - 2026-11-06 - Two-laptop system

### Added
- IN counting on laptop 1 (key button + TeethSender), OUT counting on laptop 2 (barcode scanner)
- Private link over a USB-Ethernet cable (TCP 5055, 192.168.50.1 and 192.168.50.2), print network untouched
- Plan / Target / Result / Difference dashboard, Day and Night shifts, Excel shift log
- Complete guide with 24 manual steps

[Unreleased]: https://github.com/ChornChhery/Production-Plan/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/ChornChhery/Production-Plan/releases/tag/v1.0.0
