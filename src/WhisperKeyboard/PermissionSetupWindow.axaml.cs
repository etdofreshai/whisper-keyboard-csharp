using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WhisperKeyboard;

public partial class PermissionSetupWindow : Window
{
    private static readonly IBrush GrantedBrush = new SolidColorBrush(Color.Parse("#64FF64"));
    private static readonly IBrush NotGrantedBrush = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush DisabledButtonBg = new SolidColorBrush(Color.Parse("#555555"));

    public event EventHandler? SetupComplete;

    private bool _setupCompleted;

    public PermissionSetupWindow()
    {
        InitializeComponent();
        RefreshPermissionStatus();
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

        if (hasAccessibility && hasInputMonitoring)
        {
            CheckAgainButton.Content = "All Granted!";
            CheckAgainButton.IsEnabled = false;
            ContinueButton.Content = "Continue";
            ContinueButton.Foreground = new SolidColorBrush(Colors.White);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] All macOS permissions granted");

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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

    private void OnCheckAgainClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rechecking macOS permissions...");
        RefreshPermissionStatus();
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] User chose to continue without full permissions");
        if (!_setupCompleted)
        {
            _setupCompleted = true;
            SetupComplete?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
