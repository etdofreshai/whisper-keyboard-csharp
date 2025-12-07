# WhisperKeyboard Publish Script
# Kills running instance, builds, and publishes the project

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectDir "src\WhisperKeyboard.Avalonia\WhisperKeyboard.Avalonia.csproj"

Write-Host "==> Killing existing WhisperKeyboard processes..."
Stop-Process -Name "WhisperKeyboard", "WhisperKeyboard.Avalonia" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==> Building..."
dotnet build $Project -c Release

Write-Host "==> Publishing..."
dotnet publish $Project -c Release

$PublishDir = Join-Path $ProjectDir "src\WhisperKeyboard.Avalonia\bin\Release\net8.0\publish"
Write-Host "==> Publish complete! Output: $PublishDir"
