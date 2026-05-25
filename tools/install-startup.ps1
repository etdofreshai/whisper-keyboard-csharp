# Adds (or removes) a shortcut to r400-remap-launcher.vbs in the user's
# Startup folder so the R400 remap runs hidden at every login.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File install-startup.ps1            # install
#   powershell -ExecutionPolicy Bypass -File install-startup.ps1 uninstall  # remove

param([string]$Action = "install")

$toolsDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$launcher   = Join-Path $toolsDir "r400-remap-launcher.vbs"
$startup    = [Environment]::GetFolderPath("Startup")
$shortcut   = Join-Path $startup "R400 Remap.lnk"

if ($Action -eq "uninstall") {
    if (Test-Path $shortcut) {
        Remove-Item $shortcut
        Write-Host "Removed $shortcut"
    } else {
        Write-Host "Not installed (no shortcut at $shortcut)"
    }
    exit 0
}

if (-not (Test-Path $launcher)) {
    Write-Error "Missing $launcher"
    exit 1
}

$wshShell = New-Object -ComObject WScript.Shell
$sc = $wshShell.CreateShortcut($shortcut)
$sc.TargetPath       = "wscript.exe"
$sc.Arguments        = "`"$launcher`""
$sc.WorkingDirectory = $toolsDir
$sc.WindowStyle      = 7  # minimized (vbs hides the real window anyway)
$sc.Description      = "R400 remote B -> F13 remap"
$sc.Save()

Write-Host "Installed $shortcut"
Write-Host "Starts hidden at next login. To run now without rebooting:"
Write-Host "  wscript `"$launcher`""
