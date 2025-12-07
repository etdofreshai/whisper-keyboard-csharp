namespace WhisperKeyboard;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Config _config;
    private readonly AudioProcessor _audioProcessor;
    private readonly SpeechTranscriber _transcriber;
    private readonly TextTyper _textTyper;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly RecordingIndicator _recordingIndicator;
    private readonly TranscriptionHistory _transcriptionHistory;
    private GlobalHotkey? _globalHotkey;

    private bool _isListening;
    private bool _isPaused;
    private bool _isTranscribing;
    private DateTime _ignoreUntil = DateTime.MinValue;
    private DateTime _speechStartTime;
    private CancellationTokenSource? _transcriptionCts;

    private ToolStripMenuItem _statusMenuItem = null!;
    private ToolStripMenuItem _toggleMenuItem = null!;
    private ToolStripMenuItem _pauseMenuItem = null!;

    public TrayApplicationContext()
    {
        _config = Config.Load();
        _audioProcessor = new AudioProcessor(_config);
        _transcriber = new SpeechTranscriber(_config);
        _textTyper = new TextTyper(_config);
        _hotkeyWindow = new HotkeyWindow();
        _recordingIndicator = new RecordingIndicator();
        _transcriptionHistory = new TranscriptionHistory();

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Text = "Whisper Keyboard",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += TrayIcon_DoubleClick;
        _trayIcon.Click += TrayIcon_Click;

        // Setup audio processor events
        _audioProcessor.AudioReady += AudioProcessor_AudioReady;
        _audioProcessor.VolumeChanged += AudioProcessor_VolumeChanged;
        _audioProcessor.SpeechDetected += AudioProcessor_SpeechDetected;

        // Setup recording indicator button events
        _recordingIndicator.OnPauseClicked = () =>
        {
            if (_isPaused)
                ResumeListening();
            else
                PauseListening();
        };
        _recordingIndicator.OnStopClicked = () => StopListening();
        _recordingIndicator.OnSettingsClicked = () => Settings_Click(this, EventArgs.Empty);

        // Setup hotkey window
        _hotkeyWindow.HotkeyPressed += HotkeyWindow_HotkeyPressed;

        // Register global hotkeys
        RegisterHotkeys();

        // Check if first run or missing config - show setup wizard
        if (!_config.IsCalibrated || string.IsNullOrEmpty(_config.ApiKey))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] First run detected - showing setup");
            ShowFirstRunSetup();
        }
        else
        {
            // Start listening by default
            StartListening();
        }
    }

    private void ShowFirstRunSetup()
    {
        UpdateStatus("Setup Required");

        string message = "Welcome to Whisper Keyboard!\n\n";

        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            message += "• OpenAI API key is required\n";
        }
        if (!_config.IsCalibrated)
        {
            message += "• Audio calibration is recommended\n";
        }

        message += "\nWould you like to open Settings now?";

        var result = MessageBox.Show(message, "Whisper Keyboard Setup",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            Settings_Click(this, EventArgs.Empty);
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem("Status: Idle")
        {
            Enabled = false
        };
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        _toggleMenuItem = new ToolStripMenuItem("Start Listening", null, ToggleListening_Click);
        menu.Items.Add(_toggleMenuItem);

        _pauseMenuItem = new ToolStripMenuItem("Pause", null, Pause_Click)
        {
            Enabled = false
        };
        menu.Items.Add(_pauseMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Calibrate...", null, Calibrate_Click));
        menu.Items.Add(new ToolStripMenuItem("Settings...", null, Settings_Click));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, Exit_Click));

        return menu;
    }

    private static Icon CreateDefaultIcon()
    {
        // Create a simple microphone-like icon programmatically
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Draw microphone body
            using var brush = new SolidBrush(Color.DarkGray);
            g.FillEllipse(brush, 4, 1, 8, 10);

            // Draw stand
            using var pen = new Pen(Color.DarkGray, 2);
            g.DrawLine(pen, 8, 11, 8, 14);
            g.DrawLine(pen, 4, 14, 12, 14);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private Icon CreateListeningIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.Green);
            g.FillEllipse(brush, 4, 1, 8, 10);

            using var pen = new Pen(Color.Green, 2);
            g.DrawLine(pen, 8, 11, 8, 14);
            g.DrawLine(pen, 4, 14, 12, 14);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private Icon CreateRecordingIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.Red);
            g.FillEllipse(brush, 4, 1, 8, 10);

            using var pen = new Pen(Color.Red, 2);
            g.DrawLine(pen, 8, 11, 8, 14);
            g.DrawLine(pen, 4, 14, 12, 14);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private Icon CreatePausedIcon()
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.Orange);
            g.FillEllipse(brush, 4, 1, 8, 10);

            using var pen = new Pen(Color.Orange, 2);
            g.DrawLine(pen, 8, 11, 8, 14);
            g.DrawLine(pen, 4, 14, 12, 14);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void RegisterHotkeys()
    {
        _globalHotkey = new GlobalHotkey(_hotkeyWindow.Handle);

        _globalHotkey.Register(_config.ToggleRecordingHotkey, () =>
        {
            if (_isListening)
                StopListening();
            else
                StartListening();
        });

        _globalHotkey.Register(_config.PauseResumeHotkey, () =>
        {
            if (_isPaused)
                ResumeListening();
            else
                PauseListening();
        });

        _globalHotkey.Register(_config.QuitAppHotkey, () =>
        {
            Exit_Click(this, EventArgs.Empty);
        });
    }

    private void HotkeyWindow_HotkeyPressed(object? sender, int hotkeyId)
    {
        _globalHotkey?.HandleHotkey(hotkeyId);
    }

    private void StartListening()
    {
        try
        {
            _audioProcessor.Start();
            _isListening = true;
            _isPaused = false;

            UpdateStatus("Listening...");
            _trayIcon.Icon = CreateListeningIcon();
            _toggleMenuItem.Text = "Stop Listening";
            _pauseMenuItem.Enabled = true;
            _pauseMenuItem.Text = "Pause";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Started listening - VAD threshold: {_config.VadThreshold}, Min duration: {_config.MinAudioDuration}s");

            // Show the indicator in listening mode (reset position on fresh start)
            SafeInvokeIndicator(() =>
            {
                _recordingIndicator.ResetToDefaultPosition();
                _recordingIndicator.ShowListening();
            });

            if (_config.ShowNotifications)
            {
                _trayIcon.ShowBalloonTip(1000, "Whisper Keyboard", "Now listening for speech", ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR starting: {ex.Message}");
            MessageBox.Show($"Failed to start listening: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopListening()
    {
        _audioProcessor.Stop();
        _isListening = false;
        _isPaused = false;

        // Cancel any in-flight transcription
        _transcriptionCts?.Cancel();

        UpdateStatus("Stopped");
        _trayIcon.Icon = CreateDefaultIcon();
        _toggleMenuItem.Text = "Start Listening";
        _pauseMenuItem.Enabled = false;

        SafeInvokeIndicator(() => _recordingIndicator.HideCompletely());

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Stopped listening");

        if (_config.ShowNotifications)
        {
            _trayIcon.ShowBalloonTip(1000, "Whisper Keyboard", "Stopped listening", ToolTipIcon.Info);
        }
    }

    private void PauseListening()
    {
        _audioProcessor.Pause();
        _isPaused = true;

        // Don't cancel transcription - let it complete in background
        // When resumed, it will continue from where it left off

        UpdateStatus("Paused");
        _trayIcon.Icon = CreatePausedIcon();
        _pauseMenuItem.Text = "Resume";

        SafeInvokeIndicator(() =>
        {
            _recordingIndicator.SetPauseState(true);
            _recordingIndicator.ShowPaused();
        });

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Paused");

        if (_config.ShowNotifications)
        {
            _trayIcon.ShowBalloonTip(1000, "Whisper Keyboard", "Paused", ToolTipIcon.Info);
        }
    }

    private void ResumeListening()
    {
        // Check if we were recording speech before pause
        bool wasRecording = _audioProcessor.IsSpeechDetected;

        _audioProcessor.Resume();
        _isPaused = false;
        _pauseMenuItem.Text = "Pause";

        // Restore the correct state based on what we were doing before pause
        SafeInvokeIndicator(() => _recordingIndicator.SetPauseState(false));

        if (wasRecording)
        {
            UpdateStatus("Recording...");
            _trayIcon.Icon = CreateRecordingIcon();
            SafeInvokeIndicator(() => _recordingIndicator.ShowRecording());
        }
        else
        {
            UpdateStatus("Listening...");
            _trayIcon.Icon = CreateListeningIcon();
            SafeInvokeIndicator(() => _recordingIndicator.ShowListening());
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Resumed (was recording: {wasRecording})");

        if (_config.ShowNotifications)
        {
            _trayIcon.ShowBalloonTip(1000, "Whisper Keyboard", "Resumed listening", ToolTipIcon.Info);
        }
    }

    private void UpdateStatus(string status)
    {
        _statusMenuItem.Text = $"Status: {status}";
        _trayIcon.Text = $"Whisper Keyboard - {status}";
    }

    private void SafeInvokeIndicator(Action action)
    {
        try
        {
            if (_recordingIndicator.IsDisposed) return;

            // Ensure handle is created
            if (!_recordingIndicator.IsHandleCreated)
            {
                // Force handle creation by accessing it
                var _ = _recordingIndicator.Handle;
            }

            if (_recordingIndicator.InvokeRequired)
            {
                // Use BeginInvoke (async) to avoid blocking if message pump is slow
                _recordingIndicator.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // Indicator was disposed, ignore
        }
        catch (InvalidOperationException)
        {
            // Handle not created yet, ignore
        }
    }

    private void AudioProcessor_SpeechDetected(object? sender, bool isSpeaking)
    {
        if (isSpeaking)
        {
            _speechStartTime = DateTime.Now;
            _trayIcon.Icon = CreateRecordingIcon();
            UpdateStatus("Recording...");

            // Show the recording indicator
            SafeInvokeIndicator(() => _recordingIndicator.ShowRecording());

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Speech detected - recording started");
        }
        else if (_isListening && !_isPaused)
        {
            var duration = (DateTime.Now - _speechStartTime).TotalSeconds;
            _trayIcon.Icon = CreateListeningIcon();
            UpdateStatus("Listening...");

            // Hide the recording indicator (will show again during transcription)
            SafeInvokeIndicator(() => _recordingIndicator.ShowListening());

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Speech ended - duration: {duration:F1}s");
        }
    }

    private void AudioProcessor_VolumeChanged(object? sender, double volume)
    {
        // Update the recording indicator waveform
        SafeInvokeIndicator(() => _recordingIndicator.UpdateVolume(volume));
    }

    private async void AudioProcessor_AudioReady(object? sender, byte[] audioData)
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

        UpdateStatus("Transcribing...");

        // Update indicator to show transcribing
        SafeInvokeIndicator(() => _recordingIndicator.ShowTranscribing());

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

                // Save to history
                _transcriptionHistory.AddEntry(result);

                UpdateStatus("Typing...");

                // Update indicator to show typing (safely)
                SafeInvokeIndicator(() => _recordingIndicator.ShowTyping());

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] About to call TypeTextAsync...");
                try
                {
                    await _textTyper.TypeTextAsync(result.Text);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync returned");
                }
                catch (Exception typeEx)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync EXCEPTION: {typeEx}");
                }

                // Ignore audio for 2 seconds after typing
                _ignoreUntil = DateTime.Now.AddSeconds(2);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Typed successfully, ignoring audio until {_ignoreUntil:HH:mm:ss}");

                if (_config.ShowNotifications)
                {
                    var previewText = result.Text.Length > 50 ? result.Text[..50] + "..." : result.Text;
                    _trayIcon.ShowBalloonTip(1500, "Transcribed", previewText, ToolTipIcon.None);
                }
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

            if (_config.ShowNotifications)
            {
                _trayIcon.ShowBalloonTip(2000, "Error", $"Transcription failed: {ex.Message}", ToolTipIcon.Error);
            }
        }
        finally
        {
            _isTranscribing = false;
            if (_isListening && !_isPaused)
            {
                UpdateStatus("Listening...");
                _trayIcon.Icon = CreateListeningIcon();

                // Fade back to listening mode (low opacity)
                _ = Task.Delay(500).ContinueWith(_ => SafeInvokeIndicator(() => _recordingIndicator.ShowListening()));
            }
        }
    }

    private void TrayIcon_Click(object? sender, EventArgs e)
    {
        if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
        {
            // Left click toggles listening
            if (_isListening)
            {
                if (_isPaused)
                    ResumeListening();
                else
                    PauseListening();
            }
            else
            {
                StartListening();
            }
        }
    }

    private void TrayIcon_DoubleClick(object? sender, EventArgs e)
    {
        Settings_Click(sender, e);
    }

    private void ToggleListening_Click(object? sender, EventArgs e)
    {
        if (_isListening)
            StopListening();
        else
            StartListening();
    }

    private void Pause_Click(object? sender, EventArgs e)
    {
        if (_isPaused)
            ResumeListening();
        else
            PauseListening();
    }

    private void Calibrate_Click(object? sender, EventArgs e)
    {
        // Pause listening during calibration
        bool wasListening = _isListening && !_isPaused;
        if (wasListening)
        {
            _audioProcessor.Pause();
        }

        using var wizard = new CalibrationWizard(_config);
        if (wizard.ShowDialog() == DialogResult.OK && wizard.CalibrationSuccessful)
        {
            _config.VadThreshold = wizard.CalibratedThreshold;
            _config.IsCalibrated = true;
            _config.Save();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Calibration complete - threshold: {wizard.CalibratedThreshold}");

            if (_config.ShowNotifications)
            {
                _trayIcon.ShowBalloonTip(2000, "Calibration Complete",
                    $"Voice detection threshold set to {wizard.CalibratedThreshold}", ToolTipIcon.Info);
            }
        }

        // Resume listening if it was active
        if (wasListening)
        {
            _audioProcessor.Resume();
        }
    }

    private void Settings_Click(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm(_config, _audioProcessor, _transcriptionHistory);
        settingsForm.SettingsChanged += (s, args) =>
        {
            // Reload settings if needed
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Settings changed");
        };
        settingsForm.ShowDialog();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _transcriptionCts?.Cancel();
        _transcriptionCts?.Dispose();
        _audioProcessor.Stop();
        _audioProcessor.Dispose();
        _transcriber.Dispose();
        _globalHotkey?.Dispose();
        _recordingIndicator.Dispose();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }
}

// Hidden window to receive hotkey messages
public class HotkeyWindow : Form
{
    private const int WM_HOTKEY = 0x0312;

    public event EventHandler<int>? HotkeyPressed;

    public HotkeyWindow()
    {
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Visible = false;
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(0, 0);
        Load += (s, e) => Visible = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            HotkeyPressed?.Invoke(this, hotkeyId);
        }

        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(false);
    }
}
