using System.Runtime.InteropServices;
using Avalonia;

namespace WhisperKeyboard.Avalonia;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Hide from Dock on macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSHelper.HideFromDock();
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
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
