using Avalonia.Controls;
using Avalonia.Interactivity;
using WhisperKeyboard.Core;

namespace WhisperKeyboard.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly Config _config;
    private readonly TranscriptionHistory? _history;

    public SettingsWindow() : this(Config.Load(), null) { }

    public SettingsWindow(Config config, TranscriptionHistory? history = null)
    {
        InitializeComponent();
        _config = config;
        _history = history;

        // Threshold slider label update
        ThresholdSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                ThresholdLabel.Text = ((int)ThresholdSlider.Value).ToString();
            }
        };

        // Show/hide API key toggle
        ShowApiKeyCheck.IsCheckedChanged += (s, e) =>
        {
            ApiKeyBox.PasswordChar = ShowApiKeyCheck.IsChecked == true ? '\0' : '*';
        };

        LoadSettings();
    }

    private void LoadSettings()
    {
        // API Key (only show if set in config, not from env)
        if (!string.IsNullOrEmpty(_config.ApiKey) &&
            Environment.GetEnvironmentVariable("OPENAI_API_KEY") != _config.ApiKey)
        {
            ApiKeyBox.Text = _config.ApiKey;
        }

        // Language
        for (int i = 0; i < LanguageBox.Items.Count; i++)
        {
            if (LanguageBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == _config.Language)
            {
                LanguageBox.SelectedIndex = i;
                break;
            }
        }

        // Populate audio devices
        try
        {
            var devices = OpenALNative.GetCaptureDeviceNames();
            DeviceBox.Items.Clear();
            DeviceBox.Items.Add(new ComboBoxItem { Content = "Default", Tag = -1 });
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceBox.Items.Add(new ComboBoxItem { Content = devices[i], Tag = i });
            }
            DeviceBox.SelectedIndex = _config.DeviceIndex + 1; // +1 because of "Default"
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to enumerate devices: {ex.Message}");
            DeviceBox.Items.Add(new ComboBoxItem { Content = "Default", Tag = -1 });
            DeviceBox.SelectedIndex = 0;
        }

        // Threshold
        ThresholdSlider.Value = _config.VadThreshold;
        ThresholdLabel.Text = _config.VadThreshold.ToString();

        // Audio duration settings
        MinAudioDuration.Value = (decimal)_config.MinAudioDuration;
        MaxSilenceDuration.Value = (decimal)_config.MaxSilenceDuration;

        // Typing settings
        PasteModeCheck.IsChecked = _config.PasteMode;
        PunctuationCheck.IsChecked = _config.AddPunctuation;
        CapitalizeCheck.IsChecked = _config.CapitalizeSentences;
        AutoEnterCheck.IsChecked = _config.AutoEnter;
        ExitWordsCheck.IsChecked = _config.ExitWordsEnabled;
        ExitWordsBox.Text = string.Join(", ", _config.ExitWords);

        // General settings
        ShowNotificationsCheck.IsChecked = _config.ShowNotifications;
        StartMinimizedCheck.IsChecked = _config.StartMinimized;
        ConfigPathLabel.Text = Config.GetConfigPath();

        // Hotkey settings
        ToggleRecordingHotkeyBox.Text = _config.ToggleRecordingHotkey;
        PauseResumeHotkeyBox.Text = _config.PauseResumeHotkey;
        OpenSettingsHotkeyBox.Text = _config.OpenSettingsHotkey;

        // Load history
        LoadHistory();
    }

    private void LoadHistory()
    {
        if (_history != null)
        {
            HistoryList.ItemsSource = _history.GetEntries();
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // Save API key if changed
        if (!string.IsNullOrEmpty(ApiKeyBox.Text))
        {
            _config.ApiKey = ApiKeyBox.Text;
        }

        // Language
        if (LanguageBox.SelectedItem is ComboBoxItem langItem)
        {
            _config.Language = langItem.Tag?.ToString() ?? "en";
        }

        // Device
        if (DeviceBox.SelectedItem is ComboBoxItem deviceItem)
        {
            _config.DeviceIndex = (int)(deviceItem.Tag ?? -1);
        }

        // Threshold
        _config.VadThreshold = (int)ThresholdSlider.Value;

        // Audio duration
        _config.MinAudioDuration = (double)(MinAudioDuration.Value ?? 1);
        _config.MaxSilenceDuration = (double)(MaxSilenceDuration.Value ?? 1);

        // Typing settings
        _config.PasteMode = PasteModeCheck.IsChecked ?? true;
        _config.AddPunctuation = PunctuationCheck.IsChecked ?? true;
        _config.CapitalizeSentences = CapitalizeCheck.IsChecked ?? true;
        _config.AutoEnter = AutoEnterCheck.IsChecked ?? false;
        _config.ExitWordsEnabled = ExitWordsCheck.IsChecked ?? true;
        _config.ExitWords = (ExitWordsBox.Text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // General settings
        _config.ShowNotifications = ShowNotificationsCheck.IsChecked ?? true;
        _config.StartMinimized = StartMinimizedCheck.IsChecked ?? false;

        // Hotkey settings
        _config.ToggleRecordingHotkey = ToggleRecordingHotkeyBox.Text?.Trim() ?? "";
        _config.PauseResumeHotkey = PauseResumeHotkeyBox.Text?.Trim() ?? "";
        _config.OpenSettingsHotkey = OpenSettingsHotkeyBox.Text?.Trim() ?? "";

        _config.Save();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnHistoryCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is HistoryEntry entry)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(entry.FullText);

                // Show feedback
                var originalContent = button.Content;
                button.Content = "Copied!";
                button.IsEnabled = false;

                await Task.Delay(800);

                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }
    }

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e)
    {
        _history?.Clear();
        HistoryList.ItemsSource = null;
    }
}
