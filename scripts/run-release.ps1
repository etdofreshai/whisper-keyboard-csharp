# WhisperKeyboard Release Run Script
# Kills running instance, builds, publishes, and runs from publish folder

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectDir "WhisperKeyboard.csproj"

Write-Host "==> Killing existing WhisperKeyboard processes..."
Stop-Process -Name "WhisperKeyboard" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==> Building..."
dotnet build $Project -c Release

Write-Host "==> Publishing..."
dotnet publish $Project -c Release

$PublishDir = Join-Path $ProjectDir "bin\Release\net8.0-windows\publish"
$ExePath = Join-Path $PublishDir "WhisperKeyboard.exe"

Write-Host "==> Launching..."
Start-Process -FilePath $ExePath

Write-Host "==> Done! App is running in release mode."
