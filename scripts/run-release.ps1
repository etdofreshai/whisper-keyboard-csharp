# WhisperKeyboard Release Run Script
# Kills running instance, builds, publishes, creates zip, and runs from publish folder

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Project = Join-Path $ProjectDir "src\WhisperKeyboard\WhisperKeyboard.csproj"
$PublishDir = Join-Path $ProjectDir "publish\win-x64"
$ZipPath = Join-Path $ProjectDir "publish\WhisperKeyboard-win-x64.zip"

Write-Host "==> Killing existing WhisperKeyboard processes..."
Stop-Process -Name "WhisperKeyboard", "WhisperKeyboard.Avalonia" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "==> Building..."
dotnet build $Project -c Release

Write-Host "==> Publishing for Windows x64..."
dotnet publish $Project -c Release -r win-x64 --self-contained false -o $PublishDir -p:DebugType=None -p:DebugSymbols=false

Write-Host "==> Creating zip archive..."
# Remove old zip if exists
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
# Create zip (excluding any leftover pdb files)
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal
$ZipSize = (Get-Item $ZipPath).Length / 1MB
Write-Host "==> Created: $ZipPath ($([math]::Round($ZipSize, 2)) MB)"

$ExePath = Join-Path $PublishDir "WhisperKeyboard.exe"

Write-Host "==> Launching..."
Start-Process -FilePath $ExePath

Write-Host "==> Done! App is running in release mode."
