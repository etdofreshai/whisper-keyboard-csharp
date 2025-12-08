using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

public partial class MainWindow : Window
{
    private Config _config;
    private OpenALAudioCapture? _audioCapture;
    private SpeechTranscriber? _transcriber;
    private ClipboardTextTyper? _textTyper;
    private TranscriptionHistory _history;
    private bool _isListening;
    private bool _isTranscribing;

    public MainWindow()
    {
        InitializeComponent();

        _config = Config.Load();
        _history = new TranscriptionHistory();
        _transcriber = new SpeechTranscriber(_config);

        // Check for API key
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            StatusText.Text = "API Key Required";
            StatusDetail.Text = "Set OPENAI_API_KEY environment variable";
        }
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_isListening) return;

        try
        {
            // Initialize clipboard typer
            _textTyper = new ClipboardTextTyper(_config, TopLevel.GetTopLevel(this)?.Clipboard);

            // Initialize audio capture
            _audioCapture = new OpenALAudioCapture(_config);
            _audioCapture.VolumeChanged += OnVolumeChanged;
            _audioCapture.SpeechDetected += OnSpeechDetected;
            _audioCapture.AudioReady += OnAudioReady;
            _audioCapture.Start();

            _isListening = true;
            UpdateUI();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error";
            StatusDetail.Text = ex.Message;
            Console.WriteLine($"Failed to start: {ex}");
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        StopListening();
    }

    private void StopListening()
    {
        _audioCapture?.Stop();
        _audioCapture?.Dispose();
        _audioCapture = null;
        _isListening = false;
        UpdateUI();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_config);
        settingsWindow.ShowDialog(this);
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var normalizedVolume = Math.Min(100, volume / 500 * 100);
            VolumeBar.Value = normalizedVolume;
            VolumeText.Text = $"{volume:F0}";
        });
    }

    private void OnSpeechDetected(object? sender, bool isSpeaking)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isSpeaking)
            {
                StatusText.Text = "Recording...";
                StatusDetail.Text = "Speak now";
                VolumeBar.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(255, 100, 100));
            }
            else if (!_isTranscribing)
            {
                StatusText.Text = "Listening";
                StatusDetail.Text = "Waiting for speech";
                VolumeBar.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(100, 200, 100));
            }
        });
    }

    private async void OnAudioReady(object? sender, byte[] audioData)
    {
        if (_isTranscribing || _transcriber == null) return;

        _isTranscribing = true;

        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = "Transcribing...";
            StatusDetail.Text = "Processing audio";
            VolumeBar.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(100, 100, 255));
        });

        try
        {
            var result = await _transcriber.TranscribeAsync(audioData);

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                Console.WriteLine($"Transcribed: {result.Text}");

                // Add to history
                _history.AddEntry(result);

                // Type/paste the text
                if (_textTyper != null)
                {
                    await _textTyper.TypeTextAsync(result.Text);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    HistoryList.ItemsSource = _history.GetEntries();
                    StatusText.Text = "Listening";
                    StatusDetail.Text = $"Last: {result.Text.Substring(0, Math.Min(30, result.Text.Length))}...";
                });
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText.Text = "Listening";
                    StatusDetail.Text = "No speech detected";
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Transcription error: {ex.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = "Error";
                StatusDetail.Text = ex.Message;
            });
        }
        finally
        {
            _isTranscribing = false;
        }
    }

    private void UpdateUI()
    {
        StartButton.IsEnabled = !_isListening;
        StopButton.IsEnabled = _isListening;
        StartButton.Content = _isListening ? "Listening..." : "Start Listening";

        if (_isListening)
        {
            StatusText.Text = "Listening";
            StatusDetail.Text = "Waiting for speech";
            VolumeBar.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(100, 200, 100));
        }
        else
        {
            StatusText.Text = "Ready";
            StatusDetail.Text = "Click Start to begin listening";
            VolumeBar.Value = 0;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StopListening();
        _transcriber?.Dispose();
        base.OnClosing(e);
    }
}
