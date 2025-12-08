# WhisperKeyboard Dev Run Script
# Kills running instance, builds, and runs from project files (faster iteration)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectDir "src\WhisperKeyboard\WhisperKeyboard.csproj"

Write-Host "==> Killing existing WhisperKeyboard processes..."
Stop-Process -Name "WhisperKeyboard", "WhisperKeyboard.Avalonia" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==> Building and running..."
Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", $Project, "-c", "Debug" -NoNewWindow

Write-Host "==> Done! App is running in dev mode."
