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
        Size = new Size(300, 70);

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

        _updateTimer.Start();
        _recordingTimer.Start();

        if (!Visible)
        {
            Show();
        }
        EnsureTopmost();
        Refresh(); // Force immediate repaint
    }

    public void ShowListening()
    {
        _isRecording = false;
        _statusText = "Listening...";
        _statusColor = Color.FromArgb(150, 180, 150);
        _timerText = "";

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

        _recordingTimer.Stop();
        EnsureTopmost();
        Refresh();
    }

    public void HideIndicator()
    {
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
