using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace WhisperKeyboard;

public partial class PermissionSetupWindow : Window
{
    private static readonly IBrush GrantedBrush = new SolidColorBrush(Color.Parse("#64FF64"));
    private static readonly IBrush NotGrantedBrush = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush DisabledButtonBg = new SolidColorBrush(Color.Parse("#555555"));

    public event EventHandler? SetupComplete;

    private bool _setupCompleted;
    private bool _suppressOpenAlLabel;
    private DispatcherTimer? _pollTimer;

    public PermissionSetupWindow()
    {
        InitializeComponent();
        RefreshPermissionStatus();
        StartPolling();
    }

    private void StartPolling()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += (s, e) => RefreshPermissionStatus();
        _pollTimer.Start();
    }

    private void RefreshPermissionStatus()
    {
        bool hasAccessibility = MacOSPermissions.HasAccessibilityPermission();
        bool hasInputMonitoring = MacOSPermissions.HasInputMonitoringPermission();

        // Accessibility row
        AccessibilityStatusDot.Fill = hasAccessibility ? GrantedBrush : NotGrantedBrush;
        OpenAccessibilityButton.Content = hasAccessibility ? "Granted" : "Open Settings";
        OpenAccessibilityButton.IsEnabled = !hasAccessibility;
        if (hasAccessibility)
            OpenAccessibilityButton.Background = DisabledButtonBg;

        // Input Monitoring row
        InputMonitoringStatusDot.Fill = hasInputMonitoring ? GrantedBrush : NotGrantedBrush;
        OpenInputMonitoringButton.Content = hasInputMonitoring ? "Granted" : "Open Settings";
        OpenInputMonitoringButton.IsEnabled = !hasInputMonitoring;
        if (hasInputMonitoring)
            OpenInputMonitoringButton.Background = DisabledButtonBg;

        // Audio capture backend (OpenAL / openal-soft) — re-probe in case it was
        // just installed while this window is open.
        bool hasOpenAl = OpenALNative.IsAvailable || OpenALNative.TryReload();
        OpenAlStatusDot.Fill = hasOpenAl ? GrantedBrush : NotGrantedBrush;
        if (hasOpenAl)
        {
            OpenAlButton.Content = "Installed";
            OpenAlButton.IsEnabled = false;
            OpenAlButton.Background = DisabledButtonBg;
            OpenAlDescription.Text = "Microphone capture backend is available.";
        }
        else
        {
            // Don't overwrite the transient "Copied!" confirmation set by the button handler.
            if (!_suppressOpenAlLabel)
                OpenAlButton.Content = "Copy command";
            OpenAlButton.IsEnabled = true;
            OpenAlButton.Background = new SolidColorBrush(Color.Parse("#323232"));
            OpenAlDescription.Text = "Missing. In Terminal run:  brew install openal-soft";
        }

        if (hasAccessibility && hasInputMonitoring && hasOpenAl)
        {
            CheckAgainButton.Content = "All Granted!";
            CheckAgainButton.IsEnabled = false;
            ContinueButton.Content = "Continue";
            ContinueButton.Foreground = new SolidColorBrush(Colors.White);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] All macOS permissions granted");
            _pollTimer?.Stop();

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_setupCompleted)
                    {
                        _setupCompleted = true;
                        SetupComplete?.Invoke(this, EventArgs.Empty);
                        Close();
                    }
                });
            });
        }
    }

    private void OnOpenAccessibilityClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Opening Accessibility settings");
        MacOSPermissions.OpenAccessibilitySettings();
        MacOSPermissions.RequestAccessibilityPermission();
    }

    private void OnOpenInputMonitoringClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Opening Input Monitoring settings");
        MacOSPermissions.OpenInputMonitoringSettings();
        MacOSPermissions.RequestInputMonitoringPermission();
    }

    private async void OnOpenAlButtonClick(object? sender, RoutedEventArgs e)
    {
        const string cmd = "brew install openal-soft";
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Copying openal-soft install command to clipboard");

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(cmd);
            _suppressOpenAlLabel = true;
            OpenAlButton.Content = "Copied!";
            await Task.Delay(1000);
            _suppressOpenAlLabel = false;
        }

        // Re-probe in case openal-soft was just installed, then refresh the UI.
        OpenALNative.TryReload();
        RefreshPermissionStatus();
    }

    private void OnCheckAgainClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rechecking macOS permissions...");
        RefreshPermissionStatus();
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] User chose to continue without full permissions");
        _pollTimer?.Stop();
        if (!_setupCompleted)
        {
            _setupCompleted = true;
            SetupComplete?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
