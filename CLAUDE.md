# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
dotnet build              # Build the project
dotnet run                # Run the application
dotnet publish -c Release # Create release build
```

## Architecture

Whisper Keyboard is a Windows system tray application that provides voice-to-text dictation using OpenAI's Whisper API. It runs as a single-instance WinForms app with no visible window—only a system tray icon.

### Core Components

- **TrayApplicationContext** - Main application context managing the tray icon, context menu, and coordinating all components. Handles application lifecycle and user interactions.

- **AudioProcessor** - Captures audio via NAudio, performs Voice Activity Detection (VAD) using RMS volume thresholds, and buffers speech segments. Fires `AudioReady` when speech ends.

- **SpeechTranscriber** - Sends audio to OpenAI Whisper API. Converts raw PCM to WAV format before upload.

- **TextTyper** - Outputs transcribed text via keyboard simulation. Two modes:
  - **Paste Mode** (default): Uses clipboard + Ctrl+V
  - **Typing Mode**: Uses Windows `SendInput` API with Unicode characters

- **GlobalHotkey** - Registers system-wide hotkeys via Win32 `RegisterHotKey`. Hotkeys are parsed from strings like "Ctrl+Shift+R".

- **Config** - JSON configuration stored in `%AppData%\WhisperKeyboard\config.json`. API key can also come from `OPENAI_API_KEY` environment variable.

### Data Flow

1. `AudioProcessor` continuously monitors microphone via NAudio
2. When volume exceeds `VadThreshold`, speech is detected and buffered
3. After silence duration exceeds `MaxSilenceDuration`, `AudioReady` fires
4. `TrayApplicationContext` sends audio to `SpeechTranscriber`
5. Transcribed text goes to `TextTyper` for keyboard output

### Windows API Usage

The app uses several Win32 P/Invoke calls:
- `SendInput` / `keybd_event` for keyboard simulation (TextTyper)
- `RegisterHotKey` / `UnregisterHotKey` for global hotkeys
- Clipboard API via WinForms

**Important**: The `INPUT` struct for `SendInput` must use `UIntPtr` for `dwExtraInfo` to ensure correct 64-bit alignment.
