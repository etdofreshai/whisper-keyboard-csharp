using System.Runtime.InteropServices;

namespace WhisperKeyboard;

public class RecordingIndicator : Form
{
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly System.Windows.Forms.Timer _recordingTimer;
    private bool _isRecording;
    private DateTime _recordingStartTime;
    private readonly double[] _volumeHistory;
    private int _volumeIndex;
    private const int MaxVolumeHistory = 50;
    private double _currentVolume;

    // Button events
    public Action? OnPauseClicked;
    public Action? OnStopClicked;
    public Action? OnSettingsClicked;

    // Button state
    private enum HoverButton { None, Pause, Stop, Settings }
    private HoverButton _hoverButton = HoverButton.None;
    private bool _isPausedState;
    private Rectangle _pauseButtonRect;
    private Rectangle _stopButtonRect;
    private Rectangle _settingsButtonRect;

    // Windows API for ensuring topmost
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private string _statusText = "Listening...";
    private string _timerText = "";
    private Color _statusColor = Color.FromArgb(150, 180, 150);

    // Opacity settings
    private const double IdleOpacity = 0.15;        // Very transparent when idle
    private const double ListeningOpacity = 0.25;   // Slightly visible when listening
    private const double PausedOpacity = 0.30;      // Visible when paused
    private const double ActiveOpacity = 0.95;      // Fully visible when recording/processing
    private double _targetOpacity = ListeningOpacity;
    private double _currentOpacity = ListeningOpacity;

    // Use a BufferedGraphics for smoother rendering
    private BufferedGraphicsContext _graphicsContext;
    private BufferedGraphics? _bufferedGraphics;

    public RecordingIndicator()
    {
        // Initialize volume history
        _volumeHistory = new double[MaxVolumeHistory];
        _graphicsContext = BufferedGraphicsManager.Current;

        // Form setup - borderless, topmost, centered at bottom
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(30, 30, 30);
        Size = new Size(394, 70);
        Opacity = ListeningOpacity;

        // Setup button rectangles (right side of window)
        _pauseButtonRect = new Rectangle(Width - 102, 18, 28, 28);
        _stopButtonRect = new Rectangle(Width - 70, 18, 28, 28);
        _settingsButtonRect = new Rectangle(Width - 38, 18, 28, 28);

        // Critical: Set these before creating the handle
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        UpdateStyles();

        // Position at bottom center of primary screen
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Bottom - Height - 40
        );

        // Create rounded region
        Region = CreateRoundedRegion(Width, Height, 12);

        // Update timer for animations and waveform
        _updateTimer = new System.Windows.Forms.Timer
        {
            Interval = 50
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Recording timer for duration display
        _recordingTimer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };
        _recordingTimer.Tick += RecordingTimer_Tick;

        // Force handle creation now
        CreateHandle();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW - no taskbar entry
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED - enables double buffering
            return cp;
        }
    }

    private static Region CreateRoundedRegion(int width, int height, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(width - radius * 2, height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseAllFigures();
        return new Region(path);
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Add current volume to history (circular buffer)
        _volumeHistory[_volumeIndex] = _currentVolume;
        _volumeIndex = (_volumeIndex + 1) % MaxVolumeHistory;

        // Smooth opacity transition
        if (Math.Abs(_currentOpacity - _targetOpacity) > 0.01)
        {
            // Ease toward target opacity
            _currentOpacity += (_targetOpacity - _currentOpacity) * 0.15;
            Opacity = _currentOpacity;
        }
        else if (_currentOpacity != _targetOpacity)
        {
            _currentOpacity = _targetOpacity;
            Opacity = _currentOpacity;
        }

        // Trigger repaint
        Invalidate();

        // Ensure we stay on top
        EnsureTopmost();
    }

    private void RecordingTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _recordingStartTime;
        _timerText = $"{elapsed.TotalSeconds:F1}s";
        Invalidate();
    }

    private void EnsureTopmost()
    {
        if (Visible && IsHandleCreated && !IsDisposed)
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    public void UpdateVolume(double volume)
    {
        _currentVolume = volume;
    }

    public void ShowRecording()
    {
        _isRecording = true;
        _recordingStartTime = DateTime.Now;
        _statusText = "Recording";
        _statusColor = Color.FromArgb(100, 255, 100);
        _timerText = "0.0s";
        _targetOpacity = ActiveOpacity;

        _updateTimer.Start();
        _recordingTimer.Start();

        if (!Visible)
        {
            Show();
        }
        EnsureTopmost();
        Refresh();
    }

    public void ShowListening()
    {
        _isRecording = false;
        _statusText = "Listening...";
        _statusColor = Color.FromArgb(150, 180, 150);
        _timerText = "";
        _targetOpacity = ListeningOpacity;

        _recordingTimer.Stop();
        _updateTimer.Start();

        if (!Visible)
        {
            Show();
        }
        Refresh();
    }

    public void ShowTranscribing()
    {
        _isRecording = false;
        _statusText = "Transcribing...";
        _statusColor = Color.FromArgb(150, 150, 255);
        _timerText = "";
        _targetOpacity = ActiveOpacity;

        _recordingTimer.Stop();
        EnsureTopmost();
        Refresh();
    }

    public void ShowTyping()
    {
        _isRecording = false;
        _statusText = "Typing...";
        _statusColor = Color.FromArgb(255, 255, 150);
        _timerText = "";
        _targetOpacity = ActiveOpacity;

        _recordingTimer.Stop();
        EnsureTopmost();
        Refresh();
    }

    public void ShowPaused()
    {
        _isRecording = false;
        _isPausedState = true;
        _statusText = "Paused";
        _statusColor = Color.FromArgb(255, 180, 100); // Orange
        _timerText = "";
        _targetOpacity = PausedOpacity;

        _recordingTimer.Stop();
        _updateTimer.Start(); // Keep running for smooth transitions

        if (!Visible)
        {
            Show();
        }
        EnsureTopmost();
        Refresh();
    }

    public void SetPauseState(bool isPaused)
    {
        _isPausedState = isPaused;
        Invalidate(); // Redraw to update button icon
    }

    public void HideIndicator()
    {
        // Just fade to idle opacity, don't actually hide
        _targetOpacity = IdleOpacity;
        _statusText = "Idle";
        _statusColor = Color.FromArgb(100, 100, 100);
        _timerText = "";
        _recordingTimer.Stop();
        // Keep update timer running for smooth transitions
    }

    public void HideCompletely()
    {
        // Actually hide the indicator (for pause/stop)
        _updateTimer.Stop();
        _recordingTimer.Stop();
        Hide();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        EnsureTopmost();
        Refresh();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (_pauseButtonRect.Contains(e.Location))
        {
            OnPauseClicked?.Invoke();
        }
        else if (_stopButtonRect.Contains(e.Location))
        {
            OnStopClicked?.Invoke();
        }
        else if (_settingsButtonRect.Contains(e.Location))
        {
            OnSettingsClicked?.Invoke();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var newHover = HoverButton.None;
        if (_pauseButtonRect.Contains(e.Location))
        {
            newHover = HoverButton.Pause;
        }
        else if (_stopButtonRect.Contains(e.Location))
        {
            newHover = HoverButton.Stop;
        }
        else if (_settingsButtonRect.Contains(e.Location))
        {
            newHover = HoverButton.Settings;
        }

        if (newHover != _hoverButton)
        {
            _hoverButton = newHover;
            Cursor = _hoverButton != HoverButton.None ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverButton != HoverButton.None)
        {
            _hoverButton = HoverButton.None;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Draw background
        using (var bgBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        // Draw waveform area background
        var waveformRect = new Rectangle(10, 10, 120, 50);
        using (var waveformBgBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
        {
            g.FillRectangle(waveformBgBrush, waveformRect);
        }

        // Draw waveform
        DrawWaveform(g, waveformRect);

        // Draw status text
        using (var statusFont = new Font("Segoe UI", 12, FontStyle.Regular))
        using (var statusBrush = new SolidBrush(_statusColor))
        {
            g.DrawString(_statusText, statusFont, statusBrush, 140, 12);
        }

        // Draw timer if recording
        if (!string.IsNullOrEmpty(_timerText))
        {
            using var timerFont = new Font("Segoe UI", 16, FontStyle.Bold);
            using var timerBrush = new SolidBrush(Color.FromArgb(100, 255, 100));
            g.DrawString(_timerText, timerFont, timerBrush, 140, 35);
        }

        // Draw buttons
        DrawButtons(g);

        // Draw border
        using var borderPen = new Pen(Color.FromArgb(60, 100, 60), 1);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int radius = 12;
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseAllFigures();
        g.DrawPath(borderPen, path);
    }

    private void DrawButtons(Graphics g)
    {
        // Draw pause/resume button
        var pauseBgColor = _hoverButton == HoverButton.Pause
            ? Color.FromArgb(70, 70, 70)
            : Color.FromArgb(50, 50, 50);
        using (var pauseBgBrush = new SolidBrush(pauseBgColor))
        {
            g.FillEllipse(pauseBgBrush, _pauseButtonRect);
        }

        // Draw pause icon (▐▐) or resume icon (▶)
        if (_isPausedState)
        {
            // Draw play/resume triangle
            var playColor = Color.FromArgb(100, 255, 100);
            using var playBrush = new SolidBrush(playColor);
            var cx = _pauseButtonRect.X + _pauseButtonRect.Width / 2;
            var cy = _pauseButtonRect.Y + _pauseButtonRect.Height / 2;
            var points = new Point[]
            {
                new Point(cx - 4, cy - 6),
                new Point(cx - 4, cy + 6),
                new Point(cx + 6, cy)
            };
            g.FillPolygon(playBrush, points);
        }
        else
        {
            // Draw pause bars
            var pauseColor = Color.FromArgb(255, 200, 100);
            using var pauseBrush = new SolidBrush(pauseColor);
            var cx = _pauseButtonRect.X + _pauseButtonRect.Width / 2;
            var cy = _pauseButtonRect.Y + _pauseButtonRect.Height / 2;
            g.FillRectangle(pauseBrush, cx - 6, cy - 6, 4, 12);
            g.FillRectangle(pauseBrush, cx + 2, cy - 6, 4, 12);
        }

        // Draw stop button
        var stopBgColor = _hoverButton == HoverButton.Stop
            ? Color.FromArgb(70, 70, 70)
            : Color.FromArgb(50, 50, 50);
        using (var stopBgBrush = new SolidBrush(stopBgColor))
        {
            g.FillEllipse(stopBgBrush, _stopButtonRect);
        }

        // Draw stop square
        var stopColor = Color.FromArgb(255, 100, 100);
        using var stopBrush = new SolidBrush(stopColor);
        var scx = _stopButtonRect.X + _stopButtonRect.Width / 2;
        var scy = _stopButtonRect.Y + _stopButtonRect.Height / 2;
        g.FillRectangle(stopBrush, scx - 5, scy - 5, 10, 10);

        // Draw settings button
        var settingsBgColor = _hoverButton == HoverButton.Settings
            ? Color.FromArgb(70, 70, 70)
            : Color.FromArgb(50, 50, 50);
        using (var settingsBgBrush = new SolidBrush(settingsBgColor))
        {
            g.FillEllipse(settingsBgBrush, _settingsButtonRect);
        }

        // Draw settings gear icon
        var gearColor = Color.FromArgb(180, 180, 180);
        using var gearPen = new Pen(gearColor, 1.5f);
        var gcx = _settingsButtonRect.X + _settingsButtonRect.Width / 2;
        var gcy = _settingsButtonRect.Y + _settingsButtonRect.Height / 2;

        // Draw center circle
        g.DrawEllipse(gearPen, gcx - 3, gcy - 3, 6, 6);

        // Draw gear teeth (6 lines radiating out)
        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3; // 60 degrees apart
            int x1 = gcx + (int)(5 * Math.Cos(angle));
            int y1 = gcy + (int)(5 * Math.Sin(angle));
            int x2 = gcx + (int)(8 * Math.Cos(angle));
            int y2 = gcy + (int)(8 * Math.Sin(angle));
            g.DrawLine(gearPen, x1, y1, x2, y2);
        }
    }

    private void DrawWaveform(Graphics g, Rectangle bounds)
    {
        float barWidth = (float)bounds.Width / MaxVolumeHistory;
        int centerY = bounds.Y + bounds.Height / 2;

        for (int i = 0; i < MaxVolumeHistory; i++)
        {
            // Get volume from circular buffer
            int idx = (_volumeIndex + i) % MaxVolumeHistory;
            double vol = _volumeHistory[idx];

            // Normalize volume (0-500 range to 0-1)
            double normalizedVolume = Math.Min(1.0, vol / 400.0);
            int barHeight = (int)(normalizedVolume * (bounds.Height - 6));
            barHeight = Math.Max(2, barHeight);

            // Color based on recording state and volume
            Color barColor;
            if (_isRecording)
            {
                // Bright green gradient based on volume
                int green = (int)(120 + normalizedVolume * 135);
                barColor = Color.FromArgb(50, green, 50);
            }
            else
            {
                // Muted gray-green
                barColor = Color.FromArgb(60, 90, 60);
            }

            using var brush = new SolidBrush(barColor);
            float x = bounds.X + i * barWidth;
            int y = centerY - barHeight / 2;
            g.FillRectangle(brush, x, y, Math.Max(1, barWidth - 1), barHeight);
        }

        // Draw center line
        using var centerPen = new Pen(Color.FromArgb(40, 40, 40), 1);
        g.DrawLine(centerPen, bounds.X, centerY, bounds.Right, centerY);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Allocate buffered graphics
        _bufferedGraphics = _graphicsContext.Allocate(CreateGraphics(), ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _recordingTimer.Stop();
            _recordingTimer.Dispose();
            _bufferedGraphics?.Dispose();
        }
        base.Dispose(disposing);
    }
}
