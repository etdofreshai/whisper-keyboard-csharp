using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace WhisperKeyboard.Avalonia;

public partial class RecordingIndicator : Window
{
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _recordingTimer;
    private readonly double[] _volumeHistory;
    private int _volumeIndex;
    private const int MaxVolumeHistory = 50;
    private double _currentVolume;

    private bool _isRecording;
    private DateTime _recordingStartTime;
    private bool _isPaused;

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private PixelPoint _windowStartPosition;

    // Opacity settings
    private const double IdleOpacity = 0.15;
    private const double ListeningOpacity = 0.25;
    private const double PausedOpacity = 0.30;
    private const double ActiveOpacity = 0.95;
    private double _targetOpacity = ListeningOpacity;

    // Events
    public event Action? OnPauseClicked;
    public event Action? OnStopClicked;
    public event Action? OnSettingsClicked;

    public RecordingIndicator()
    {
        InitializeComponent();

        _volumeHistory = new double[MaxVolumeHistory];

        // Setup timers
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _updateTimer.Tick += UpdateTimer_Tick;

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _recordingTimer.Tick += RecordingTimer_Tick;

        // Wire up button events
        PauseButton.Click += (s, e) => OnPauseClicked?.Invoke();
        StopButton.Click += (s, e) => OnStopClicked?.Invoke();
        SettingsButton.Click += (s, e) => OnSettingsClicked?.Invoke();

        // Enable dragging
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        // Position at bottom center of screen
        Opened += (s, e) => ResetToDefaultPosition();
    }

    public void ResetToDefaultPosition()
    {
        if (Screens.Primary is { } screen)
        {
            var workArea = screen.WorkingArea;
            Position = new PixelPoint(
                workArea.X + (workArea.Width - (int)Width) / 2,
                workArea.Y + workArea.Height - (int)Height - 40
            );
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            // Check if clicking on a button area (buttons are on the right)
            var pos = point.Position;
            if (pos.X < Width - 120) // Not in button area
            {
                _isDragging = true;
                _dragStartPoint = pos;
                _windowStartPosition = Position;
                e.Handled = true;
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _dragStartPoint;
            Position = new PixelPoint(
                _windowStartPosition.X + (int)delta.X,
                _windowStartPosition.Y + (int)delta.Y
            );
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Add current volume to history
        _volumeHistory[_volumeIndex] = _currentVolume;
        _volumeIndex = (_volumeIndex + 1) % MaxVolumeHistory;

        // Smooth opacity transition
        var currentOpacity = Opacity;
        if (Math.Abs(currentOpacity - _targetOpacity) > 0.01)
        {
            Opacity = currentOpacity + (_targetOpacity - currentOpacity) * 0.15;
        }
        else if (Math.Abs(currentOpacity - _targetOpacity) > 0.001)
        {
            Opacity = _targetOpacity;
        }

        // Redraw waveform
        DrawWaveform();
    }

    private void RecordingTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _recordingStartTime;
        TimerText.Text = $"{elapsed.TotalSeconds:F1}s";
    }

    private void DrawWaveform()
    {
        WaveformCanvas.Children.Clear();

        var width = WaveformCanvas.Bounds.Width;
        var height = WaveformCanvas.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var barWidth = width / MaxVolumeHistory;
        var centerY = height / 2;

        for (int i = 0; i < MaxVolumeHistory; i++)
        {
            int idx = (_volumeIndex + i) % MaxVolumeHistory;
            double vol = _volumeHistory[idx];

            // Normalize volume (0-500 range to 0-1)
            double normalizedVolume = Math.Min(1.0, vol / 400.0);
            double barHeight = Math.Max(2, normalizedVolume * (height - 6));

            // Color based on recording state
            Color barColor;
            if (_isRecording)
            {
                int green = (int)(120 + normalizedVolume * 135);
                barColor = Color.FromRgb(50, (byte)green, 50);
            }
            else
            {
                barColor = Color.FromRgb(60, 90, 60);
            }

            var rect = new Rectangle
            {
                Width = Math.Max(1, barWidth - 1),
                Height = barHeight,
                Fill = new SolidColorBrush(barColor)
            };

            Canvas.SetLeft(rect, i * barWidth);
            Canvas.SetTop(rect, centerY - barHeight / 2);
            WaveformCanvas.Children.Add(rect);
        }

        // Draw center line
        var centerLine = new Line
        {
            StartPoint = new Point(0, centerY),
            EndPoint = new Point(width, centerY),
            Stroke = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            StrokeThickness = 1
        };
        WaveformCanvas.Children.Add(centerLine);
    }

    public void UpdateVolume(double volume)
    {
        _currentVolume = volume;
    }

    public void ShowRecording()
    {
        _isRecording = true;
        _recordingStartTime = DateTime.Now;
        StatusText.Text = "Recording";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));
        TimerText.Text = "0.0s";
        _targetOpacity = ActiveOpacity;

        _updateTimer.Start();
        _recordingTimer.Start();

        if (!IsVisible) Show();
    }

    public void ShowListening()
    {
        _isRecording = false;
        _isPaused = false;
        StatusText.Text = "Listening...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(150, 180, 150));
        TimerText.Text = "";
        _targetOpacity = ListeningOpacity;

        UpdatePauseButtonState();
        _recordingTimer.Stop();
        _updateTimer.Start();

        if (!IsVisible) Show();
    }

    public void ShowTranscribing()
    {
        _isRecording = false;
        StatusText.Text = "Transcribing...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 255));
        TimerText.Text = "";
        _targetOpacity = ActiveOpacity;

        _recordingTimer.Stop();
    }

    public void ShowTyping()
    {
        _isRecording = false;
        StatusText.Text = "Typing...";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 150));
        TimerText.Text = "";
        _targetOpacity = ActiveOpacity;

        _recordingTimer.Stop();
    }

    public void ShowPaused()
    {
        _isRecording = false;
        _isPaused = true;
        StatusText.Text = "Paused";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 100));
        TimerText.Text = "";
        _targetOpacity = PausedOpacity;

        UpdatePauseButtonState();
        _recordingTimer.Stop();
        _updateTimer.Start();

        if (!IsVisible) Show();
    }

    public void SetPauseState(bool isPaused)
    {
        _isPaused = isPaused;
        UpdatePauseButtonState();
    }

    private void UpdatePauseButtonState()
    {
        PauseIcon.IsVisible = !_isPaused;
        PlayIcon.IsVisible = _isPaused;
    }

    public void HideCompletely()
    {
        _updateTimer.Stop();
        _recordingTimer.Stop();
        Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Prevent closing, just hide
        e.Cancel = true;
        HideCompletely();
    }
}
