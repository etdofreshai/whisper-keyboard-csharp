#!/bin/bash

# WhisperKeyboard Dev Run Script
# Kills running instance, builds, and runs from project files (faster iteration)

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/WhisperKeyboard.Avalonia.csproj"

echo "==> Killing existing WhisperKeyboard processes..."
killall WhisperKeyboard.Avalonia 2>/dev/null || true
sleep 0.3

echo "==> Building and running..."
dotnet run --project "$PROJECT" -c Debug &

echo "==> Done! App is running in dev mode."
