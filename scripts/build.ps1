# WhisperKeyboard Build Script
# Kills running instance and builds the project

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectDir "src\WhisperKeyboard.Avalonia\WhisperKeyboard.Avalonia.csproj"

Write-Host "==> Killing existing WhisperKeyboard processes..."
Stop-Process -Name "WhisperKeyboard", "WhisperKeyboard.Avalonia" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==> Building..."
dotnet build $Project -c Release

Write-Host "==> Build complete!"
