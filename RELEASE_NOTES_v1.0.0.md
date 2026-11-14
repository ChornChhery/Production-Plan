## Teeth 1.0.0 - first stable release

Count what goes **IN** and what comes **OUT** of a production line, compare it with the plan on a big screen, and keep an Excel log of every shift. Two laptops, one cable, no router, no internet.

### Highlights
- 🔢 IN counting on laptop 1 (key button) and OUT counting on laptop 2 (barcode scanner)
- 📊 Plan / Target / Result / Difference on a big screen, Day and Night shifts, Excel shift log
- 🛡️ No lost presses: presses wait in `pending.txt` and are counted once each
- 🩺 One-click setup and self-repair (**Teeth Doctor**), including the .NET 4.8 install and a time zone check
- 💾 Counts survive restarts and crashes, also when Teeth starts with a new empty database
- 🔐 Configurable admin PIN
- ✅ 113 automatic checks run on every push; acceptance tests D1 to D15 passed on real laptops

### Download
Download `Teeth_Project_2Laptops_FINAL.zip` below, unzip it (both zips) to `C:\Teeth` on **both** laptops, then follow the [Quick start](https://github.com/ChornChhery/Production-Plan#quick-start).

### Requirements
Windows 10/11 64-bit, .NET Framework 4.8 (installed by the tool if missing), 2 USB-Ethernet adapters, 1 Ethernet cable.

See [CHANGELOG.md](https://github.com/ChornChhery/Production-Plan/blob/main/CHANGELOG.md) for the full history.
