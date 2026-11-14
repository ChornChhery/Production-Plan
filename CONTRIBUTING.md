# Contributing to Production Plan (Teeth)

Thank you for helping. Small fixes, documentation and ideas are all welcome.

## Ways to help
- **Report a bug** with the *Bug report* form (attach `App Files\Logs\LAST-STATUS.txt`).
- **Suggest a feature** with the *Feature request* form.
- **Improve the docs**: the guides in `docs/` and the PDFs in the main folder.
- **Send a pull request** (see below).

## Set up
1. Fork the repository and clone it to a Windows PC (Windows 10/11, 64-bit).
2. Open `App Files\Source\Teeth\Teeth.csproj` and `App Files\Source_Sender\TeethSender\TeethSender.csproj` in Visual Studio 2019 or newer (.NET Framework 4.8), or build with `msbuild`.
3. Run the Doctor tests before and after your change (from `App Files\Source_Build\doctor-tests`):
   ```powershell
   powershell -NoProfile -File Test-Doctor-Static.ps1
   powershell -NoProfile -File Test-Doctor-Logic.ps1
   powershell -NoProfile -File Test-Doctor-Scenarios.ps1
   ```

## Rules for `Teeth-Doctor.ps1` and the `.bat` files
- Must run on **Windows PowerShell 5.1** (no PowerShell 7-only syntax).
- Plain **ASCII** text and **CRLF** line endings (the static test checks this).
- Never change the built-in Ethernet port or the print software. Only the USB adapter that the tool found.
- A silent run (the scheduled task) must never show a window or ask a question.
- New behaviour needs a test in the logic or scenario suite.

## Pull requests
1. Create a branch: `git checkout -b feature/short-name`
2. Keep the change focused; update the README or the guides if behaviour changes.
3. Commit with a clear message (`Fix: ...`, `Add: ...`, `Docs: ...`).
4. Open a pull request and fill in the checklist. CI must be green.

## Never commit real data
`Teeth.sqlite`, `Log Data\*.xlsx`, `pending.txt` and your `.ini` files stay on the laptop; the `.gitignore` excludes them.

By contributing you agree that your work is released under the [MIT License](LICENSE).
