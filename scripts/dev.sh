#!/bin/bash

# WhisperKeyboard Dev Script
# Kills running instance, builds, publishes, and runs the app

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/WhisperKeyboard.Avalonia.csproj"

echo "==> Killing existing WhisperKeyboard processes..."
pkill -f "WhisperKeyboard.Avalonia" 2>/dev/null || true
sleep 0.5

echo "==> Building..."
dotnet build "$PROJECT" -c Release

echo "==> Publishing..."
if [[ "$OSTYPE" == "darwin"* ]]; then
    dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained false
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/bin/Release/net8.0/osx-arm64/publish"
    APP_PATH="$PUBLISH_DIR/WhisperKeyboard.Avalonia.app"

    if [[ -d "$APP_PATH" ]]; then
        echo "==> Launching app bundle..."
        open "$APP_PATH"
    else
        echo "==> Launching from publish folder..."
        "$PUBLISH_DIR/WhisperKeyboard.Avalonia" &
    fi
else
    dotnet publish "$PROJECT" -c Release
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/bin/Release/net8.0/publish"
    echo "==> Launching..."
    "$PUBLISH_DIR/WhisperKeyboard.Avalonia" &
fi

echo "==> Done! App is running."
