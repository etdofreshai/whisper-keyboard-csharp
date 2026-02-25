using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

public partial class CalibrationWindow : Window
{
    private readonly IAudioCapture _audioCapture;
    private readonly Config _config;
    private readonly DispatcherTimer _phaseTimer;
    private readonly List<double> _silenceSamples = new();
    private readonly List<double> _speechSamples = new();
    private double _currentVolume;

    private enum CalibrationPhase { Waiting, Silence, Speech, Done }
    private CalibrationPhase _phase = CalibrationPhase.Waiting;
    private DateTime _phaseStartTime;
    private const int SilenceDurationSecs = 3;
    private const int SpeechDurationSecs = 5;

    public event EventHandler<int>? CalibrationComplete;

    public CalibrationWindow()
    {
        InitializeComponent();
        _config = Config.Load();
        _audioCapture = new NAudioCapture(_config);
        _phaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
    }

    public CalibrationWindow(IAudioCapture audioCapture, Config config)
    {
        InitializeComponent();
        _audioCapture = audioCapture;
        _config = config;

        _phaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _phaseTimer.Tick += OnPhaseTimerTick;

        _audioCapture.VolumeChanged += OnVolumeChanged;

        Opened += (s, e) => StartCalibration();
        Closed += (s, e) =>
        {
            _phaseTimer.Stop();
            _audioCapture.VolumeChanged -= OnVolumeChanged;
        };
    }

    private void StartCalibration()
    {
        _phase = CalibrationPhase.Silence;
        _phaseStartTime = DateTime.Now;
        _silenceSamples.Clear();

        InstructionText.Text = "Please stay quiet...";
        SubInstructionText.Text = "Measuring background noise level";
        CountdownText.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));

        _phaseTimer.Start();
    }

    private void OnVolumeChanged(object? sender, double volume)
    {
        _currentVolume = volume;

        if (_phase == CalibrationPhase.Silence)
        {
            _silenceSamples.Add(volume);
        }
        else if (_phase == CalibrationPhase.Speech)
        {
            _speechSamples.Add(volume);
        }
    }

    private void OnPhaseTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _phaseStartTime;

        switch (_phase)
        {
            case CalibrationPhase.Silence:
            {
                var remaining = SilenceDurationSecs - (int)elapsed.TotalSeconds;
                CountdownText.Text = remaining > 0 ? $"{remaining}" : "...";
                DrawVolumeBar();

                if (elapsed.TotalSeconds >= SilenceDurationSecs)
                {
                    // Transition to speech phase
                    _phase = CalibrationPhase.Speech;
                    _phaseStartTime = DateTime.Now;
                    _speechSamples.Clear();

                    InstructionText.Text = "Please speak normally";
                    SubInstructionText.Text = "Read aloud: \"The quick brown fox jumps over the lazy dog\"";
                    CountdownText.Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 100));

                    var silencePeak = _silenceSamples.Count > 0 ? _silenceSamples.Max() : 0;
                    var silenceAvg = _silenceSamples.Count > 0 ? _silenceSamples.Average() : 0;
                    ValuesText.Text = $"Silence: avg {silenceAvg:F0}, peak {silencePeak:F0}";
                }
                break;
            }

            case CalibrationPhase.Speech:
            {
                var remaining = SpeechDurationSecs - (int)elapsed.TotalSeconds;
                CountdownText.Text = remaining > 0 ? $"{remaining}" : "...";
                DrawVolumeBar();

                if (elapsed.TotalSeconds >= SpeechDurationSecs)
                {
                    FinishCalibration();
                }
                break;
            }
        }
    }

    private void FinishCalibration()
    {
        _phase = CalibrationPhase.Done;
        _phaseTimer.Stop();

        var silencePeak = _silenceSamples.Count > 0 ? _silenceSamples.Max() : 100;
        var silenceAvg = _silenceSamples.Count > 0 ? _silenceSamples.Average() : 50;

        // Filter speech samples to only include values above silence peak (actual speech)
        var speechValues = _speechSamples.Where(v => v > silencePeak).ToList();
        var speechMin = speechValues.Count > 0 ? speechValues.Min() : silencePeak * 2;
        var speechAvg = speechValues.Count > 0 ? speechValues.Average() : silencePeak * 3;

        // Calculate threshold: biased toward silence to avoid false negatives
        // Place threshold at 30% between silence peak and speech minimum
        var threshold = (int)(silencePeak + (speechMin - silencePeak) * 0.3);

        // Clamp to valid range
        threshold = Math.Clamp(threshold, 10, 5000);

        // Apply to config
        _config.VadThreshold = threshold;
        _config.Save();

        InstructionText.Text = "Calibration complete!";
        SubInstructionText.Text = "";
        CountdownText.Text = "";
        CountdownText.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));
        ValuesText.Text = $"Silence peak: {silencePeak:F0} | Speech avg: {speechAvg:F0} | Threshold set to: {threshold}";

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Calibration: silence_avg={silenceAvg:F0}, silence_peak={silencePeak:F0}, speech_min={speechMin:F0}, speech_avg={speechAvg:F0}, threshold={threshold}");

        CalibrationComplete?.Invoke(this, threshold);

        // Auto-close after 2 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Dispatcher.UIThread.Post(() => Close());
        });
    }

    private void DrawVolumeBar()
    {
        CalibrationVolumeCanvas.Children.Clear();

        var width = CalibrationVolumeCanvas.Bounds.Width;
        var height = CalibrationVolumeCanvas.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        // Background
        var bg = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30))
        };
        CalibrationVolumeCanvas.Children.Add(bg);

        // Volume bar (scale to 2000 for visual range)
        var maxDisplay = 2000.0;
        var volumeRatio = Math.Min(1.0, _currentVolume / maxDisplay);
        var volumeWidth = volumeRatio * width;

        if (volumeWidth > 0)
        {
            var barColor = _phase == CalibrationPhase.Silence
                ? Color.FromRgb(80, 180, 80)   // Green for silence phase
                : Color.FromRgb(220, 140, 60);  // Orange for speech phase

            var bar = new Rectangle
            {
                Width = volumeWidth,
                Height = height,
                Fill = new SolidColorBrush(barColor)
            };
            CalibrationVolumeCanvas.Children.Add(bar);
        }
    }
}
