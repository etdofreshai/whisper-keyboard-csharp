namespace WhisperKeyboard;

public class SettingsForm : Form
{
    private readonly Config _config;
    private readonly AudioProcessor _audioProcessor;
    private readonly TranscriptionHistory? _transcriptionHistory;
    private TabControl _tabControl = null!;

    // Audio tab controls
    private ComboBox _deviceComboBox = null!;
    private TrackBar _vadThresholdTrackBar = null!;
    private Label _vadThresholdLabel = null!;
    private NumericUpDown _minAudioDurationNumeric = null!;
    private NumericUpDown _maxSilenceDurationNumeric = null!;

    // OpenAI tab controls
    private TextBox _apiKeyTextBox = null!;
    private ComboBox _languageComboBox = null!;

    // Typing tab controls
    private NumericUpDown _typingSpeedNumeric = null!;
    private CheckBox _pasteModeCheckBox = null!;
    private CheckBox _addPunctuationCheckBox = null!;
    private CheckBox _capitalizeSentencesCheckBox = null!;
    private CheckBox _autoEnterCheckBox = null!;
    private CheckBox _exitWordsEnabledCheckBox = null!;
    private TextBox _exitWordsTextBox = null!;

    // Hotkeys tab controls
    private TextBox _toggleRecordingHotkeyTextBox = null!;
    private TextBox _pauseResumeHotkeyTextBox = null!;

    // General tab controls
    private CheckBox _showNotificationsCheckBox = null!;
    private CheckBox _startMinimizedCheckBox = null!;
    private CheckBox _startWithWindowsCheckBox = null!;

    // History tab controls
    private Panel _historyEntriesPanel = null!;
    private Label _historyEmptyLabel = null!;
    private Button _historyClearAllButton = null!;

    public event EventHandler? SettingsChanged;

    public SettingsForm(Config config, AudioProcessor audioProcessor, TranscriptionHistory? transcriptionHistory = null)
    {
        _config = config;
        _audioProcessor = audioProcessor;
        _transcriptionHistory = transcriptionHistory;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Whisper Keyboard Settings";
        Size = new Size(450, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(10, 5)
        };

        CreateAudioTab();
        CreateOpenAITab();
        CreateTypingTab();
        CreateHotkeysTab();
        CreateGeneralTab();
        CreateHistoryTab();

        Controls.Add(_tabControl);

        // Add buttons panel at bottom
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50
        };

        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(80, 30),
            Location = new Point(Width - 200, 10)
        };
        saveButton.Click += SaveButton_Click;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 30),
            Location = new Point(Width - 110, 10)
        };
        cancelButton.Click += (s, e) => Close();

        buttonPanel.Controls.AddRange(new Control[] { saveButton, cancelButton });
        Controls.Add(buttonPanel);
    }

    private void CreateAudioTab()
    {
        var tab = new TabPage("Audio");
        var y = 20;

        // Audio device selection
        tab.Controls.Add(new Label { Text = "Audio Device:", Location = new Point(15, y), AutoSize = true });
        _deviceComboBox = new ComboBox
        {
            Location = new Point(15, y + 20),
            Size = new Size(380, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var devices = _audioProcessor.GetAudioDevices();
        _deviceComboBox.Items.Add("Default Device");
        foreach (var device in devices)
        {
            _deviceComboBox.Items.Add(device);
        }

        tab.Controls.Add(_deviceComboBox);
        y += 55;

        // VAD threshold
        tab.Controls.Add(new Label { Text = "Voice Detection Threshold:", Location = new Point(15, y), AutoSize = true });
        _vadThresholdLabel = new Label { Location = new Point(330, y), AutoSize = true };
        tab.Controls.Add(_vadThresholdLabel);

        _vadThresholdTrackBar = new TrackBar
        {
            Location = new Point(15, y + 20),
            Size = new Size(380, 45),
            Minimum = 10,
            Maximum = 5000,
            TickFrequency = 500,
            LargeChange = 100,
            SmallChange = 10
        };
        _vadThresholdTrackBar.ValueChanged += (s, e) => _vadThresholdLabel.Text = _vadThresholdTrackBar.Value.ToString();
        tab.Controls.Add(_vadThresholdTrackBar);
        y += 65;

        // Calibrate button
        var calibrateButton = new Button
        {
            Text = "Run Calibration Wizard...",
            Location = new Point(15, y),
            Size = new Size(180, 28)
        };
        calibrateButton.Click += CalibrateButton_Click;
        tab.Controls.Add(calibrateButton);
        y += 40;

        // Min audio duration
        tab.Controls.Add(new Label { Text = "Min Audio Duration (seconds):", Location = new Point(15, y), AutoSize = true });
        _minAudioDurationNumeric = new NumericUpDown
        {
            Location = new Point(220, y - 3),
            Size = new Size(80, 25),
            Minimum = 0.5m,
            Maximum = 10m,
            DecimalPlaces = 1,
            Increment = 0.1m
        };
        tab.Controls.Add(_minAudioDurationNumeric);
        y += 35;

        // Max silence duration
        tab.Controls.Add(new Label { Text = "Max Silence Duration (seconds):", Location = new Point(15, y), AutoSize = true });
        _maxSilenceDurationNumeric = new NumericUpDown
        {
            Location = new Point(220, y - 3),
            Size = new Size(80, 25),
            Minimum = 0.3m,
            Maximum = 5m,
            DecimalPlaces = 1,
            Increment = 0.1m
        };
        tab.Controls.Add(_maxSilenceDurationNumeric);

        _tabControl.TabPages.Add(tab);
    }

    private void CreateOpenAITab()
    {
        var tab = new TabPage("OpenAI");
        var y = 20;

        // API Key
        tab.Controls.Add(new Label { Text = "API Key:", Location = new Point(15, y), AutoSize = true });
        _apiKeyTextBox = new TextBox
        {
            Location = new Point(15, y + 20),
            Size = new Size(380, 25),
            UseSystemPasswordChar = true
        };
        tab.Controls.Add(_apiKeyTextBox);
        y += 55;

        // Show/Hide API key checkbox
        var showApiKeyCheckBox = new CheckBox
        {
            Text = "Show API Key",
            Location = new Point(15, y),
            AutoSize = true
        };
        showApiKeyCheckBox.CheckedChanged += (s, e) => _apiKeyTextBox.UseSystemPasswordChar = !showApiKeyCheckBox.Checked;
        tab.Controls.Add(showApiKeyCheckBox);
        y += 35;

        // Language selection
        tab.Controls.Add(new Label { Text = "Language:", Location = new Point(15, y), AutoSize = true });
        _languageComboBox = new ComboBox
        {
            Location = new Point(15, y + 20),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _languageComboBox.Items.AddRange(new object[]
        {
            "auto - Auto Detect",
            "en - English",
            "es - Spanish",
            "fr - French",
            "de - German",
            "it - Italian",
            "pt - Portuguese",
            "ru - Russian",
            "ja - Japanese",
            "ko - Korean",
            "zh - Chinese"
        });
        tab.Controls.Add(_languageComboBox);

        _tabControl.TabPages.Add(tab);
    }

    private void CreateTypingTab()
    {
        var tab = new TabPage("Typing");
        var y = 20;

        // Typing speed
        tab.Controls.Add(new Label { Text = "Typing Speed (seconds per character):", Location = new Point(15, y), AutoSize = true });
        _typingSpeedNumeric = new NumericUpDown
        {
            Location = new Point(260, y - 3),
            Size = new Size(80, 25),
            Minimum = 0.001m,
            Maximum = 0.5m,
            DecimalPlaces = 3,
            Increment = 0.005m
        };
        tab.Controls.Add(_typingSpeedNumeric);
        y += 40;

        // Paste mode
        _pasteModeCheckBox = new CheckBox
        {
            Text = "Use Paste Mode (faster, uses clipboard)",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_pasteModeCheckBox);
        y += 30;

        // Add punctuation
        _addPunctuationCheckBox = new CheckBox
        {
            Text = "Auto-add punctuation at end of sentences",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_addPunctuationCheckBox);
        y += 30;

        // Capitalize sentences
        _capitalizeSentencesCheckBox = new CheckBox
        {
            Text = "Capitalize first letter of sentences",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_capitalizeSentencesCheckBox);
        y += 30;

        // Auto enter
        _autoEnterCheckBox = new CheckBox
        {
            Text = "Auto-press Enter after typing",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_autoEnterCheckBox);
        y += 40;

        // Exit words
        _exitWordsEnabledCheckBox = new CheckBox
        {
            Text = "Enable exit words (press Enter when spoken):",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_exitWordsEnabledCheckBox);
        y += 25;
        _exitWordsTextBox = new TextBox
        {
            Location = new Point(15, y),
            Size = new Size(380, 25)
        };
        tab.Controls.Add(_exitWordsTextBox);
        y += 25;
        tab.Controls.Add(new Label
        {
            Text = "Words like \"over\", \"enter\", \"submit\" - removed from text, then Enter is pressed",
            Location = new Point(15, y),
            Size = new Size(380, 20),
            ForeColor = Color.Gray
        });

        _tabControl.TabPages.Add(tab);
    }

    private void CreateHotkeysTab()
    {
        var tab = new TabPage("Hotkeys");
        var y = 20;

        // Toggle recording hotkey
        tab.Controls.Add(new Label { Text = "Toggle Recording:", Location = new Point(15, y), AutoSize = true });
        _toggleRecordingHotkeyTextBox = new TextBox
        {
            Location = new Point(150, y - 3),
            Size = new Size(150, 25)
        };
        tab.Controls.Add(_toggleRecordingHotkeyTextBox);
        y += 40;

        // Pause/Resume hotkey
        tab.Controls.Add(new Label { Text = "Pause/Resume:", Location = new Point(15, y), AutoSize = true });
        _pauseResumeHotkeyTextBox = new TextBox
        {
            Location = new Point(150, y - 3),
            Size = new Size(150, 25)
        };
        tab.Controls.Add(_pauseResumeHotkeyTextBox);
        y += 50;

        tab.Controls.Add(new Label
        {
            Text = "Note: Hotkey changes require app restart.",
            Location = new Point(15, y),
            AutoSize = true,
            ForeColor = Color.Gray
        });

        _tabControl.TabPages.Add(tab);
    }

    private void CreateGeneralTab()
    {
        var tab = new TabPage("General");
        var y = 20;

        // Show notifications
        _showNotificationsCheckBox = new CheckBox
        {
            Text = "Show notifications",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_showNotificationsCheckBox);
        y += 30;

        // Start minimized
        _startMinimizedCheckBox = new CheckBox
        {
            Text = "Start minimized to tray",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_startMinimizedCheckBox);
        y += 30;

        // Start with Windows
        _startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(15, y),
            AutoSize = true
        };
        tab.Controls.Add(_startWithWindowsCheckBox);
        y += 50;

        // Config file location
        tab.Controls.Add(new Label
        {
            Text = $"Config file: {Config.GetConfigPath()}",
            Location = new Point(15, y),
            Size = new Size(380, 40),
            ForeColor = Color.Gray
        });

        _tabControl.TabPages.Add(tab);
    }

    private void CreateHistoryTab()
    {
        var tab = new TabPage("History");

        // Scrollable panel for entries
        _historyEntriesPanel = new Panel
        {
            Location = new Point(15, 15),
            Size = new Size(395, 250),
            AutoScroll = true,
            BackColor = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle
        };
        tab.Controls.Add(_historyEntriesPanel);

        // Empty state label
        _historyEmptyLabel = new Label
        {
            Text = "No transcriptions yet",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 10),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Visible = false
        };
        _historyEntriesPanel.Controls.Add(_historyEmptyLabel);

        // Clear All button
        _historyClearAllButton = new Button
        {
            Text = "Clear All",
            Location = new Point(15, 275),
            Size = new Size(85, 28)
        };
        _historyClearAllButton.Click += HistoryClearAllButton_Click;
        tab.Controls.Add(_historyClearAllButton);

        // Info label
        tab.Controls.Add(new Label
        {
            Text = "Double-click an entry to copy it to clipboard",
            Location = new Point(110, 280),
            AutoSize = true,
            ForeColor = Color.Gray
        });

        _tabControl.TabPages.Add(tab);

        // Load history entries
        LoadHistoryEntries();
    }

    private void LoadHistoryEntries()
    {
        _historyEntriesPanel.Controls.Clear();

        if (_transcriptionHistory == null || _transcriptionHistory.Count == 0)
        {
            _historyEmptyLabel.Visible = true;
            _historyEntriesPanel.Controls.Add(_historyEmptyLabel);
            _historyClearAllButton.Enabled = false;
            return;
        }

        _historyEmptyLabel.Visible = false;
        _historyClearAllButton.Enabled = true;

        var entries = _transcriptionHistory.GetEntries();
        int yOffset = 5;

        foreach (var entry in entries)
        {
            var entryPanel = CreateHistoryEntryPanel(entry, yOffset);
            _historyEntriesPanel.Controls.Add(entryPanel);
            yOffset += 50;
        }
    }

    private Panel CreateHistoryEntryPanel(HistoryEntry entry, int yOffset)
    {
        var panel = new Panel
        {
            Location = new Point(5, yOffset),
            Size = new Size(365, 45),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        // Timestamp label
        var timeLabel = new Label
        {
            Text = entry.FormattedTime,
            Location = new Point(8, 4),
            Size = new Size(55, 16),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8)
        };
        panel.Controls.Add(timeLabel);

        // Preview text label
        var textLabel = new Label
        {
            Text = entry.PreviewText,
            Location = new Point(8, 20),
            Size = new Size(280, 20),
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9)
        };
        // Show full text in tooltip
        var tooltip = new ToolTip();
        tooltip.SetToolTip(textLabel, entry.FullText);
        panel.Controls.Add(textLabel);

        // Copy button
        var copyButton = new Button
        {
            Text = "Copy",
            Location = new Point(295, 10),
            Size = new Size(60, 24),
            Tag = entry
        };
        copyButton.Click += HistoryCopyButton_Click;
        panel.Controls.Add(copyButton);

        // Double-click to copy
        panel.DoubleClick += (s, e) => CopyHistoryEntry(entry, null);
        textLabel.DoubleClick += (s, e) => CopyHistoryEntry(entry, null);

        return panel;
    }

    private void HistoryCopyButton_Click(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Tag is HistoryEntry entry)
        {
            CopyHistoryEntry(entry, button);
        }
    }

    private void CopyHistoryEntry(HistoryEntry entry, Button? button)
    {
        try
        {
            Clipboard.SetText(entry.FullText);

            if (button != null)
            {
                var originalText = button.Text;
                button.Text = "Copied!";
                button.Enabled = false;

                var timer = new System.Windows.Forms.Timer { Interval = 800 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!button.IsDisposed)
                    {
                        button.Text = originalText;
                        button.Enabled = true;
                    }
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HistoryClearAllButton_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all transcription history?",
            "Clear History",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _transcriptionHistory?.Clear();
            LoadHistoryEntries();
        }
    }

    private void LoadSettings()
    {
        // Audio
        _deviceComboBox.SelectedIndex = _config.DeviceIndex + 1; // +1 for "Default Device"
        _vadThresholdTrackBar.Value = Math.Clamp(_config.VadThreshold, 10, 5000);
        _vadThresholdLabel.Text = _vadThresholdTrackBar.Value.ToString();
        _minAudioDurationNumeric.Value = (decimal)_config.MinAudioDuration;
        _maxSilenceDurationNumeric.Value = (decimal)_config.MaxSilenceDuration;

        // OpenAI
        _apiKeyTextBox.Text = _config.ApiKey;
        var languageIndex = _languageComboBox.Items.Cast<string>()
            .ToList()
            .FindIndex(s => s.StartsWith(_config.Language));
        _languageComboBox.SelectedIndex = languageIndex >= 0 ? languageIndex : 0;

        // Typing
        _typingSpeedNumeric.Value = (decimal)_config.TypingSpeed;
        _pasteModeCheckBox.Checked = _config.PasteMode;
        _addPunctuationCheckBox.Checked = _config.AddPunctuation;
        _capitalizeSentencesCheckBox.Checked = _config.CapitalizeSentences;
        _autoEnterCheckBox.Checked = _config.AutoEnter;
        _exitWordsEnabledCheckBox.Checked = _config.ExitWordsEnabled;
        _exitWordsTextBox.Text = string.Join(", ", _config.ExitWords);

        // Hotkeys
        _toggleRecordingHotkeyTextBox.Text = _config.ToggleRecordingHotkey;
        _pauseResumeHotkeyTextBox.Text = _config.PauseResumeHotkey;

        // General
        _showNotificationsCheckBox.Checked = _config.ShowNotifications;
        _startMinimizedCheckBox.Checked = _config.StartMinimized;
        _startWithWindowsCheckBox.Checked = _config.StartWithWindows;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        // Audio
        _config.DeviceIndex = _deviceComboBox.SelectedIndex - 1; // -1 for "Default Device"
        _config.VadThreshold = _vadThresholdTrackBar.Value;
        _config.MinAudioDuration = (double)_minAudioDurationNumeric.Value;
        _config.MaxSilenceDuration = (double)_maxSilenceDurationNumeric.Value;

        // OpenAI
        _config.ApiKey = _apiKeyTextBox.Text;
        var selectedLanguage = _languageComboBox.SelectedItem?.ToString() ?? "en - English";
        _config.Language = selectedLanguage.Split(' ')[0];

        // Typing
        _config.TypingSpeed = (double)_typingSpeedNumeric.Value;
        _config.PasteMode = _pasteModeCheckBox.Checked;
        _config.AddPunctuation = _addPunctuationCheckBox.Checked;
        _config.CapitalizeSentences = _capitalizeSentencesCheckBox.Checked;
        _config.AutoEnter = _autoEnterCheckBox.Checked;
        _config.ExitWordsEnabled = _exitWordsEnabledCheckBox.Checked;
        _config.ExitWords = _exitWordsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Hotkeys
        _config.ToggleRecordingHotkey = _toggleRecordingHotkeyTextBox.Text;
        _config.PauseResumeHotkey = _pauseResumeHotkeyTextBox.Text;

        // General
        _config.ShowNotifications = _showNotificationsCheckBox.Checked;
        _config.StartMinimized = _startMinimizedCheckBox.Checked;
        _config.StartWithWindows = _startWithWindowsCheckBox.Checked;

        // Handle start with Windows
        SetStartWithWindows(_config.StartWithWindows);

        _config.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        MessageBox.Show("Settings saved successfully!", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            if (enable)
            {
                key.SetValue("WhisperKeyboard", Application.ExecutablePath);
            }
            else
            {
                key.DeleteValue("WhisperKeyboard", false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting startup: {ex.Message}");
        }
    }

    private void CalibrateButton_Click(object? sender, EventArgs e)
    {
        using var wizard = new CalibrationWizard(_config);
        if (wizard.ShowDialog() == DialogResult.OK && wizard.CalibrationSuccessful)
        {
            _vadThresholdTrackBar.Value = Math.Clamp(wizard.CalibratedThreshold, 10, 5000);
            _vadThresholdLabel.Text = _vadThresholdTrackBar.Value.ToString();
            _config.IsCalibrated = true;
        }
    }
}
