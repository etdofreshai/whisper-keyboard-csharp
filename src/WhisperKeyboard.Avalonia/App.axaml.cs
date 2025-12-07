using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WhisperKeyboard.Avalonia;

public partial class App : Application
{
    private WhisperKeyboardApp? _app;
    private Window? _hiddenWindow;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create a hidden window to keep the app running and provide clipboard access
            _hiddenWindow = new Window
            {
                Width = 0,
                Height = 0,
                ShowInTaskbar = false,
                WindowState = WindowState.Minimized,
                SystemDecorations = SystemDecorations.None,
                IsVisible = false
            };
            desktop.MainWindow = _hiddenWindow;

            // Don't shutdown when the hidden window closes
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Initialize the main app controller
            _app = new WhisperKeyboardApp(desktop);

            // Create tray icon programmatically
            var trayMenu = new NativeMenu();
            _app.SetupTrayMenu(trayMenu);

            // Load tray icon from embedded resource
            WindowIcon? icon = null;
            try
            {
                var iconUri = new Uri("avares://WhisperKeyboard.Avalonia/Assets/tray-icon.png");
                using var stream = AssetLoader.Open(iconUri);
                var bitmap = new Bitmap(stream);
                icon = new WindowIcon(bitmap);
                Console.WriteLine($"Loaded tray icon: {bitmap.Size.Width}x{bitmap.Size.Height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load tray icon: {ex.Message}");
            }

            _trayIcon = new TrayIcon
            {
                ToolTipText = "Whisper Keyboard",
                Icon = icon,
                Menu = trayMenu,
                IsVisible = true
            };

            // Add to application's tray icons
            var icons = new TrayIcons { _trayIcon };
            SetValue(TrayIcon.IconsProperty, icons);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
