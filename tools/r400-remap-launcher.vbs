' Hidden launcher for r400-remap.ps1.
' Runs the PowerShell remap with no visible window. Drop a shortcut to
' this file into shell:startup to start at login.

Set shell = CreateObject("WScript.Shell")
scriptDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
shell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptDir & "\r400-remap.ps1""", 0, False
