#!/bin/bash

# WhisperKeyboard Release Run Script
# Kills running instance, builds, publishes, and runs from publish folder

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$PROJECT_DIR/src/WhisperKeyboard/WhisperKeyboard.csproj"

echo "==> Killing existing WhisperKeyboard processes..."
killall WhisperKeyboard 2>/dev/null || true
sleep 0.3

echo "==> Building..."
dotnet build "$PROJECT" -c Release -p:UseSharedCompilation=false

echo "==> Publishing..."
if [[ "$OSTYPE" == "darwin"* ]]; then
    dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained false -p:UseSharedCompilation=false
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard/bin/Release/net8.0/osx-arm64/publish"
    APP_PATH="$PUBLISH_DIR/WhisperKeyboard.app"

    if [[ -d "$APP_PATH" ]]; then
        echo "==> Launching app bundle..."
        open "$APP_PATH"
    else
        echo "==> Launching from publish folder..."
        "$PUBLISH_DIR/WhisperKeyboard" &
    fi
else
    dotnet publish "$PROJECT" -c Release
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard/bin/Release/net8.0/publish"
    echo "==> Launching..."
    "$PUBLISH_DIR/WhisperKeyboard" &
fi

echo "==> Done! App is running in release mode."
