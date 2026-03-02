using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WhisperKeyboard;

/// <summary>
/// Centralized macOS permission checks and System Settings deep links.
/// Uses AXIsProcessTrusted() for Accessibility and CGEventTapCreate() probe for Input Monitoring,
/// since CGPreflight* functions cache their results per-process and won't detect runtime changes.
/// </summary>
public static class MacOSPermissions
{
    // AXIsProcessTrusted updates at runtime (unlike CGPreflightPostEventAccess which caches)
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGRequestPostEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGRequestListenEventAccess();

    // CGEventTapCreate probe for Input Monitoring — returns NULL if permission not granted
    private delegate IntPtr CGEventTapCallbackProbe(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventTapCreate(
        int tap, int place, int options, ulong eventsOfInterest,
        CGEventTapCallbackProbe callback, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    private const int kCGSessionEventTap = 1;
    private const int kCGHeadInsertEventTap = 0;
    private const int kCGEventTapOptionListenOnly = 1;

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

    // Keep the callback delegate alive to prevent GC during CGEventTapCreate probe
    private static readonly CGEventTapCallbackProbe ProbeCallback = ProbeCallbackImpl;

    private static IntPtr ProbeCallbackImpl(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo)
        => eventRef;

    public static bool HasAccessibilityPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        return AXIsProcessTrusted();
    }

    public static bool HasInputMonitoringPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        return ProbeInputMonitoring();
    }

    /// <summary>
    /// Probes Input Monitoring permission by attempting to create a minimal event tap.
    /// Unlike CGPreflightListenEventAccess(), this reflects the current permission state.
    /// </summary>
    private static bool ProbeInputMonitoring()
    {
        try
        {
            // Try to create a listen-only event tap for key down events
            var tap = CGEventTapCreate(
                kCGSessionEventTap, kCGHeadInsertEventTap,
                kCGEventTapOptionListenOnly,
                1UL << 10, // kCGEventKeyDown = 10
                ProbeCallback, IntPtr.Zero);

            if (tap == IntPtr.Zero)
                return false;

            // Permission granted — clean up the probe tap immediately
            CFRelease(tap);
            return true;
        }
        catch
        {
            return false;
        }
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
