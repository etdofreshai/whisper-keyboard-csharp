#!/bin/bash

# WhisperKeyboard Publish Script
# Kills running instance, builds, publishes, and creates macOS .app bundle

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/WhisperKeyboard.Avalonia.csproj"

echo "==> Killing existing WhisperKeyboard processes..."
killall WhisperKeyboard.Avalonia 2>/dev/null || true
sleep 0.3

echo "==> Building..."
dotnet build "$PROJECT" -c Release -p:UseSharedCompilation=false

echo "==> Publishing..."
if [[ "$OSTYPE" == "darwin"* ]]; then
    dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained false -p:UseSharedCompilation=false
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/bin/Release/net8.0/osx-arm64/publish"

    echo "==> Creating macOS app bundle..."
    APP_NAME="WhisperKeyboard.app"
    APP_PATH="$PROJECT_DIR/publish/$APP_NAME"
    MACOS_RESOURCES="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/macOS"

    # Remove old app bundle if it exists
    rm -rf "$APP_PATH"

    # Create app bundle directory structure
    mkdir -p "$APP_PATH/Contents/MacOS"
    mkdir -p "$APP_PATH/Contents/Resources"

    # Copy all published files to MacOS folder
    cp -R "$PUBLISH_DIR"/* "$APP_PATH/Contents/MacOS/"

    # Copy Info.plist
    if [[ -f "$MACOS_RESOURCES/Info.plist" ]]; then
        cp "$MACOS_RESOURCES/Info.plist" "$APP_PATH/Contents/Info.plist"
    else
        echo "Warning: Info.plist not found at $MACOS_RESOURCES/Info.plist"
    fi

    # Copy app icon
    if [[ -f "$MACOS_RESOURCES/AppIcon.icns" ]]; then
        cp "$MACOS_RESOURCES/AppIcon.icns" "$APP_PATH/Contents/Resources/"
    else
        echo "Warning: AppIcon.icns not found at $MACOS_RESOURCES/AppIcon.icns"
    fi

    echo "==> App bundle created: $APP_PATH"
else
    dotnet publish "$PROJECT" -c Release
    PUBLISH_DIR="$PROJECT_DIR/src/WhisperKeyboard.Avalonia/bin/Release/net8.0/publish"
    echo "==> Publish complete! Output: $PUBLISH_DIR"
fi
