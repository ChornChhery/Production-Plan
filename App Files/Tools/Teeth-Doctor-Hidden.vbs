' Starts Teeth-Doctor.ps1 with NO window (used by the scheduled task, so nothing flashes on the shop-floor screen
' and no window can take the keyboard focus away from the scanner or the key button).
Option Explicit
Dim sh, fso, here, cmd
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
here = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & here & "\Teeth-Doctor.ps1"" -Mode Fix -Silent"
sh.Run cmd, 0, False
