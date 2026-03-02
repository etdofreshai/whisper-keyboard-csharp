using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WhisperKeyboard;

/// <summary>
/// Centralized macOS permission checks and System Settings deep links.
/// </summary>
public static class MacOSPermissions
{
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGPreflightPostEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGRequestPostEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGPreflightListenEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGRequestListenEventAccess();

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_retPtr(IntPtr receiver, IntPtr selector);

    private const string AccessibilitySettingsUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
    private const string InputMonitoringSettingsUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent";

    public static bool HasAccessibilityPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        return CGPreflightPostEventAccess();
    }

    public static bool HasInputMonitoringPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        return CGPreflightListenEventAccess();
    }

    public static void RequestAccessibilityPermission()
    {
        if (OperatingSystem.IsMacOS())
            CGRequestPostEventAccess();
    }

    public static void RequestInputMonitoringPermission()
    {
        if (OperatingSystem.IsMacOS())
            CGRequestListenEventAccess();
    }

    public static bool AnyPermissionMissing()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        return !HasAccessibilityPermission() || !HasInputMonitoringPermission();
    }

    public static bool IsRunningFromAppBundle()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        return GetAppBundlePath() != null;
    }

    public static void OpenAccessibilitySettings()
    {
        try
        {
            Process.Start("open", AccessibilitySettingsUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to open Accessibility settings: {ex.Message}");
        }
    }

    public static void OpenInputMonitoringSettings()
    {
        try
        {
            Process.Start("open", InputMonitoringSettingsUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to open Input Monitoring settings: {ex.Message}");
        }
    }

    private static string? GetAppBundlePath()
    {
        try
        {
            var cls = objc_getClass("NSBundle");
            if (cls == IntPtr.Zero) return null;
            var bundle = objc_msgSend_retPtr(cls, sel_registerName("mainBundle"));
            if (bundle == IntPtr.Zero) return null;
            var pathObj = objc_msgSend_retPtr(bundle, sel_registerName("bundlePath"));
            if (pathObj == IntPtr.Zero) return null;
            var path = Marshal.PtrToStringUTF8(
                objc_msgSend_retPtr(pathObj, sel_registerName("UTF8String")));
            return path != null && path.EndsWith(".app") ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
