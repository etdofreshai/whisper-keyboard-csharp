using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WhisperKeyboard.Core;

namespace WhisperKeyboard.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly Config _config;
    private readonly TranscriptionHistory? _history;
    private TextBox? _activeHotkeyBox;

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

    private void OnClearHotkeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            switch (tag)
            {
                case "ToggleRecording":
                    ToggleRecordingHotkeyBox.Clear();
                    break;
                case "PauseResume":
                    PauseResumeHotkeyBox.Clear();
                    break;
                case "OpenSettings":
                    OpenSettingsHotkeyBox.Clear();
                    break;
            }
        }
    }

    private void OnHotkeyBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _activeHotkeyBox = textBox;
            textBox.BorderBrush = new SolidColorBrush(Color.Parse("#0078D4"));
            textBox.BorderThickness = new global::Avalonia.Thickness(2);
            if (string.IsNullOrEmpty(textBox.Text))
            {
                textBox.Watermark = "Press a key combination...";
            }
        }
    }

    private void OnHotkeyBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _activeHotkeyBox = null;
            textBox.BorderBrush = new SolidColorBrush(Color.Parse("#444444"));
            textBox.BorderThickness = new global::Avalonia.Thickness(1);
            textBox.Watermark = "Click to set hotkey";
        }
    }

    private void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        e.Handled = true;

        // Escape clears the hotkey
        if (e.Key == Key.Escape)
        {
            textBox.Text = "";
            return;
        }

        // Ignore modifier-only key presses
        if (IsModifierKey(e.Key))
        {
            return;
        }

        // Build the hotkey string
        var hotkey = BuildHotkeyString(e.KeyModifiers, e.Key);
        if (!string.IsNullOrEmpty(hotkey))
        {
            textBox.Text = hotkey;
        }
    }

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LWin || key == Key.RWin;
    }

    private static string BuildHotkeyString(KeyModifiers modifiers, Key key)
    {
        var parts = new List<string>();

        // Add modifiers in standard order (macOS convention)
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Cmd");
        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Option");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");

        // Convert key to display string
        var keyName = GetKeyName(key);
        if (!string.IsNullOrEmpty(keyName))
        {
            parts.Add(keyName);
        }

        // Require at least one modifier for a valid hotkey
        if (parts.Count < 2)
        {
            return "";
        }

        return string.Join("+", parts);
    }

    private static string? GetKeyName(Key key)
    {
        return key switch
        {
            // Letters
            Key.A => "A", Key.B => "B", Key.C => "C", Key.D => "D", Key.E => "E",
            Key.F => "F", Key.G => "G", Key.H => "H", Key.I => "I", Key.J => "J",
            Key.K => "K", Key.L => "L", Key.M => "M", Key.N => "N", Key.O => "O",
            Key.P => "P", Key.Q => "Q", Key.R => "R", Key.S => "S", Key.T => "T",
            Key.U => "U", Key.V => "V", Key.W => "W", Key.X => "X", Key.Y => "Y",
            Key.Z => "Z",

            // Numbers
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
            Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",

            // Function keys
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",

            // Special keys
            Key.Space => "Space",
            Key.Return => "Return",
            Key.Tab => "Tab",
            Key.Delete => "Delete",
            Key.Back => "Backspace",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",

            // Punctuation
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemBackslash => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemTilde => "`",
            Key.OemQuestion => "/",

            _ => null
        };
    }
}
