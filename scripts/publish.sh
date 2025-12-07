#!/bin/bash

# WhisperKeyboard Publish Script
# Kills running instance, builds, and publishes the project

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
else
    dotnet publish "$PROJECT" -c Release
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/bin/Release/net8.0/publish"
fi

echo "==> Publish complete! Output: $PUBLISH_DIR"
