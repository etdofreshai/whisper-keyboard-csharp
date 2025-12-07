# WhisperKeyboard Publish Script
# Kills running instance, builds, and publishes the project

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
Write-Host "==> Publish complete! Output: $PublishDir"
