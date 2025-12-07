using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

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

            // Hide from Dock on macOS (must be done after everything is set up)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use a timer to call this after the event loop starts
                DispatcherTimer.RunOnce(() => MacOSHelper.HideFromDock(), TimeSpan.FromMilliseconds(100));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// macOS native interop for hiding app from Dock
/// </summary>
internal static partial class MacOSHelper
{
    // NSApplicationActivationPolicy values
    private const long NSApplicationActivationPolicyAccessory = 1;

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string className);

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string selectorName);

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg);

    public static void HideFromDock()
    {
        try
        {
            // Get [NSApplication sharedApplication]
            var nsAppClass = objc_getClass("NSApplication");
            var sharedAppSel = sel_registerName("sharedApplication");
            var nsApp = objc_msgSend(nsAppClass, sharedAppSel);

            // [nsApp setActivationPolicy:NSApplicationActivationPolicyAccessory]
            var setActivationPolicySel = sel_registerName("setActivationPolicy:");
            objc_msgSend_long(nsApp, setActivationPolicySel, NSApplicationActivationPolicyAccessory);

            Console.WriteLine("Set activation policy to Accessory (hidden from Dock)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to hide from Dock: {ex.Message}");
        }
    }
}
