#!/bin/bash

# WhisperKeyboard Build Script
# Kills running instance and builds the project

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/WhisperKeyboard.Avalonia.csproj"

echo "==> Killing existing WhisperKeyboard processes..."
pkill -f "WhisperKeyboard.Avalonia" 2>/dev/null || true
sleep 0.5

echo "==> Building..."
dotnet build "$PROJECT" -c Release

echo "==> Build complete!"
