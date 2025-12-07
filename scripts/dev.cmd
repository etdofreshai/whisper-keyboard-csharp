@echo off
setlocal

REM WhisperKeyboard Dev Script
REM Kills running instance, builds, publishes, and runs the app

set SCRIPT_DIR=%~dp0
set PROJECT_DIR=%SCRIPT_DIR%..
set PROJECT=%PROJECT_DIR%\src\WhisperKeyboard.Avalonia\WhisperKeyboard.Avalonia.csproj

echo ==^> Killing existing WhisperKeyboard processes...
powershell -NoProfile -Command "Stop-Process -Name WhisperKeyboard* -Force -ErrorAction SilentlyContinue"
timeout /t 1 /nobreak >nul

echo ==^> Building...
dotnet build "%PROJECT%" -c Release
if errorlevel 1 goto :error

echo ==^> Publishing...
dotnet publish "%PROJECT%" -c Release
if errorlevel 1 goto :error

set PUBLISH_DIR=%PROJECT_DIR%\src\WhisperKeyboard.Avalonia\bin\Release\net8.0\publish

echo ==^> Launching...
start "" "%PUBLISH_DIR%\WhisperKeyboard.Avalonia.exe"

echo ==^> Done! App is running.
goto :end

:error
echo Build or publish failed!
exit /b 1

:end
endlocal
