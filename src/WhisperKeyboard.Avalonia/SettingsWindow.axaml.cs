using Avalonia.Controls;
using Avalonia.Interactivity;
using WhisperKeyboard.Core;

namespace WhisperKeyboard.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly Config _config;

    public SettingsWindow() : this(Config.Load()) { }

    public SettingsWindow(Config config)
    {
        InitializeComponent();
        _config = config;

        ThresholdSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                ThresholdLabel.Text = ((int)ThresholdSlider.Value).ToString();
            }
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
            using var tempCapture = new OpenALAudioCapture(_config);
            var devices = tempCapture.GetAudioDevices();
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

        // Text settings
        PunctuationCheck.IsChecked = _config.AddPunctuation;
        CapitalizeCheck.IsChecked = _config.CapitalizeSentences;
        ExitWordsCheck.IsChecked = _config.ExitWordsEnabled;
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

        // Text settings
        _config.AddPunctuation = PunctuationCheck.IsChecked ?? true;
        _config.CapitalizeSentences = CapitalizeCheck.IsChecked ?? true;
        _config.ExitWordsEnabled = ExitWordsCheck.IsChecked ?? true;

        _config.Save();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
