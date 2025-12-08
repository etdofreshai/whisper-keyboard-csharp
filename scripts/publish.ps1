# WhisperKeyboard Publish Script
# Builds and creates distributable zip for Windows x64

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
# Publish without debug symbols for smaller size
dotnet publish $Project -c Release -r win-x64 --self-contained false -o $PublishDir -p:DebugType=None -p:DebugSymbols=false

Write-Host "==> Creating zip archive..."
# Remove old zip if exists
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
# Create zip
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$ZipSize = (Get-Item $ZipPath).Length / 1MB
$FolderSize = (Get-ChildItem $PublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "==> Publish complete!"
Write-Host "    Folder: $PublishDir ($([math]::Round($FolderSize, 2)) MB)"
Write-Host "    Zip:    $ZipPath ($([math]::Round($ZipSize, 2)) MB)"
Write-Host ""
Write-Host "Note: Requires .NET 8 Runtime to be installed on target machine."
Write-Host "      Download from: https://dotnet.microsoft.com/download/dotnet/8.0"
