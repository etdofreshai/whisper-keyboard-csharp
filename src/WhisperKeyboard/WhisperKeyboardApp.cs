using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

/// <summary>
/// Main application controller that manages the tray icon, recording indicator,
/// settings window, and coordinates audio capture/transcription.
/// </summary>
public class WhisperKeyboardApp : IDisposable
{
    private readonly Config _config;
    private readonly IAudioCapture _audioCapture;
    private readonly SpeechTranscriber _transcriber;
    private readonly ClipboardTextTyper _textTyper;
    private readonly TranscriptionHistory _history;
    private readonly RecordingIndicator _recordingIndicator;
    private readonly GlobalHotkey _globalHotkey;

    private bool _isListening;
    private bool _isPaused;
    private bool _isTranscribing;
    private DateTime _ignoreUntil = DateTime.MinValue;
    private bool _disposed;
    private CancellationTokenSource? _transcriptionCts;

    // Tray icon (defined in App.axaml)
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _toggleMenuItem;
    private NativeMenuItem? _pauseMenuItem;

    public WhisperKeyboardApp(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _config = Config.Load();
        _history = new TranscriptionHistory();
        _transcriber = new SpeechTranscriber(_config);
        
        if (OperatingSystem.IsWindows())
        {
            _audioCapture = new NAudioCapture(_config);
        }
        else
        {
            _audioCapture = new OpenALAudioCapture(_config);
        }

        _recordingIndicator = new RecordingIndicator();
        _globalHotkey = new GlobalHotkey();

        // Get clipboard from the main window (we'll create a hidden one)
        var clipboard = desktop.MainWindow?.Clipboard;
        _textTyper = new ClipboardTextTyper(_config, clipboard);

        // Setup audio events
        _audioCapture.VolumeChanged += OnVolumeChanged;
        _audioCapture.SpeechDetected += OnSpeechDetected;
        _audioCapture.AudioReady += OnAudioReady;
        _audioCapture.AudioTooShort += OnAudioTooShort;

        // Setup recording indicator events
        _recordingIndicator.OnPauseClicked += () =>
        {
            if (_isPaused)
                ResumeListening();
            else
                PauseListening();
        };
        _recordingIndicator.OnStopClicked += StopListening;
        _recordingIndicator.OnSettingsClicked += ShowSettings;

        // Register global hotkeys
        RegisterHotkeys();

        // Start listening automatically if configured
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            Console.WriteLine("API Key not configured. Please set OPENAI_API_KEY or configure in settings.");
        }
        else if (_config.StartListeningOnLaunch)
        {
            StartListening();
        }
    }

    private void RegisterHotkeys()
    {
        // Toggle recording hotkey
        if (!string.IsNullOrWhiteSpace(_config.ToggleRecordingHotkey))
        {
            _globalHotkey.Register(_config.ToggleRecordingHotkey, ToggleListening);
        }

        // Pause/Resume hotkey
        if (!string.IsNullOrWhiteSpace(_config.PauseResumeHotkey))
        {
            _globalHotkey.Register(_config.PauseResumeHotkey, () =>
            {
                if (_isPaused)
                    ResumeListening();
                else if (_isListening)
                    PauseListening();
            });
        }

        // Open settings hotkey
        if (!string.IsNullOrWhiteSpace(_config.OpenSettingsHotkey))
        {
            _globalHotkey.Register(_config.OpenSettingsHotkey, ShowSettings);
        }
    }

    public void SetupTrayMenu(NativeMenu menu)
    {
        menu.Items.Clear();

        _statusMenuItem = new NativeMenuItem("Status: Idle") { IsEnabled = false };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        _toggleMenuItem = new NativeMenuItem("Start Listening");
        _toggleMenuItem.Click += (s, e) => ToggleListening();
        menu.Items.Add(_toggleMenuItem);

        _pauseMenuItem = new NativeMenuItem("Pause") { IsEnabled = false };
        _pauseMenuItem.Click += (s, e) =>
        {
            if (_isPaused) ResumeListening();
            else PauseListening();
        };
        menu.Items.Add(_pauseMenuItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (s, e) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (s, e) => Quit();
        menu.Items.Add(quitItem);
    }

    private void ToggleListening()
    {
        if (_isListening)
            StopListening();
        else
            StartListening();
    }

    public void StartListening()
    {
        if (_isListening) return;

        try
        {
            _audioCapture.Start();
            _isListening = true;
            _isPaused = false;

            UpdateStatus("Listening...");
            UpdateMenuState();

            _recordingIndicator.ResetToDefaultPosition();
            _recordingIndicator.ShowListening();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Started listening");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR starting: {ex.Message}");
        }
    }

    public void StopListening()
    {
        if (!_isListening) return;

        _audioCapture.Stop();
        _isListening = false;
        _isPaused = false;

        // Cancel any in-flight transcription
        _transcriptionCts?.Cancel();

        UpdateStatus("Stopped");
        UpdateMenuState();

        _recordingIndicator.HideCompletely();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stopped listening");
    }

    public void PauseListening()
    {
        if (!_isListening || _isPaused) return;

        _audioCapture.Pause();
        _isPaused = true;

        // Don't cancel transcription - let it complete in background
        // When resumed, it will continue from where it left off

        UpdateStatus("Paused");
        UpdateMenuState();

        _recordingIndicator.SetPauseState(true);
        _recordingIndicator.ShowPaused();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Paused");
    }

    public void ResumeListening()
    {
        if (!_isListening || !_isPaused) return;

        // Check if we were recording speech before pause
        bool wasRecording = _audioCapture.IsSpeechDetected;

        _audioCapture.Resume();
        _isPaused = false;

        _recordingIndicator.SetPauseState(false);

        // Restore the correct state based on what we were doing before pause
        if (wasRecording)
        {
            UpdateStatus("Recording...");
            _recordingIndicator.ShowRecording();
        }
        else
        {
            UpdateStatus("Listening...");
            _recordingIndicator.ShowListening();
        }

        UpdateMenuState();

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Resumed (was recording: {wasRecording})");
    }

    private void UpdateStatus(string status)
    {
        if (_statusMenuItem != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _statusMenuItem.Header = $"Status: {status}";
            });
        }
    }

    private void UpdateMenuState()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_toggleMenuItem != null)
            {
                _toggleMenuItem.Header = _isListening ? "Stop Listening" : "Start Listening";
            }
            if (_pauseMenuItem != null)
            {
                _pauseMenuItem.IsEnabled = _isListening;
                _pauseMenuItem.Header = _isPaused ? "Resume" : "Pause";
            }
        });
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _recordingIndicator.UpdateVolume(volume);
        });
    }

    private bool _showingTooShort;

    private void OnSpeechDetected(object? sender, bool isSpeaking)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isSpeaking)
            {
                UpdateStatus("Recording...");
                _recordingIndicator.ShowRecording();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Speech detected - recording started");
            }
            else if (_isListening && !_isPaused && !_isTranscribing && !_showingTooShort)
            {
                UpdateStatus("Listening...");
                _recordingIndicator.ShowListening();
            }
        });
    }

    private void OnAudioTooShort(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            _showingTooShort = true;
            _recordingIndicator.ShowTooShort();
            await Task.Delay(1000); // Keep flag active long enough for ShowTooShort to complete
            _showingTooShort = false;
        });
    }

    private async void OnAudioReady(object? sender, byte[] audioData)
    {
        if (_isTranscribing)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Skipping - already transcribing");
            return;
        }
        if (DateTime.Now < _ignoreUntil)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Skipping - in ignore period");
            return;
        }

        _isTranscribing = true;

        // Create a new cancellation token source for this transcription
        _transcriptionCts?.Dispose();
        _transcriptionCts = new CancellationTokenSource();
        var cancellationToken = _transcriptionCts.Token;

        Dispatcher.UIThread.Post(() =>
        {
            UpdateStatus("Transcribing...");
            _recordingIndicator.ShowTranscribing();
        });

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Audio ready - {audioData.Length} bytes, sending to API...");

        try
        {
            var result = await _transcriber.TranscribeAsync(audioData, cancellationToken);

            // Check if we were cancelled or stopped before typing
            // Note: paused is OK - transcription should complete if user spoke before pausing
            if (cancellationToken.IsCancellationRequested || !_isListening)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcription completed but cancelled/stopped - not typing");
                return;
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcribed: \"{result.Text}\"");

                _history.AddEntry(result);

                Dispatcher.UIThread.Post(() =>
                {
                    UpdateStatus("Typing...");
                    _recordingIndicator.ShowTyping();
                });

                await _textTyper.TypeTextAsync(result.Text);

                // Ignore audio for 2 seconds after typing
                _ignoreUntil = DateTime.Now.AddSeconds(2);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Typed successfully");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcription returned empty result");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcription cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcription error: {ex.Message}");
        }
        finally
        {
            _isTranscribing = false;

            if (_isListening && !_isPaused)
            {
                // Delay before returning to listening mode
                await Task.Delay(500);
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateStatus("Listening...");
                    _recordingIndicator.ShowListening();
                });
            }
        }
    }

    public void ShowSettings()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var settingsWindow = new SettingsWindow(_config, _history);
            settingsWindow.Show();
        });
    }

    public void Quit()
    {
        Dispose();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _globalHotkey.Dispose();
        _audioCapture.Stop();
        _audioCapture.Dispose();
        _transcriber.Dispose();

        _disposed = true;
    }
}
