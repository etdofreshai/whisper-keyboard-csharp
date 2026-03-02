using System.Runtime.InteropServices;

namespace WhisperKeyboard;

/// <summary>
/// macOS key simulation using CGEvent APIs.
/// Requires Accessibility permission to post events.
/// </summary>
public static class MacOSInputSender
{
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, bool keyDown);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern void CGEventSetFlags(IntPtr eventRef, ulong flags);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern void CGEventPost(int tap, IntPtr eventRef);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern void CGEventKeyboardSetUnicodeString(IntPtr eventRef, int length, [MarshalAs(UnmanagedType.LPWStr)] string str);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern IntPtr CGEventSourceCreate(int stateID);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGPreflightPostEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGRequestPostEventAccess();

    // CGEventTapLocation
    private const int kCGSessionEventTap = 1;

    // CGEventSourceStateID
    private const int kCGEventSourceStateHIDSystemState = 1;

    // CGEvent modifier flags
    private const ulong kCGEventFlagMaskCommand = 0x00100000;

    // macOS virtual key codes
    private const ushort kVK_Return = 0x24;     // 36
    private const ushort kVK_Delete = 0x33;     // 51 (backspace)
    private const ushort kVK_V = 0x09;          // 9

    private static bool _permissionChecked;

    private static void EnsurePermission()
    {
        if (_permissionChecked) return;
        _permissionChecked = true;

        if (!CGPreflightPostEventAccess())
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CGEvent post permission not granted. Requesting...");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Go to System Settings > Privacy & Security > Accessibility and add this app.");
            CGRequestPostEventAccess();
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CGEvent post permission granted (Accessibility)");
        }
    }

    /// <summary>
    /// Simulate Cmd+V paste.
    /// </summary>
    public static void SendPaste()
    {
        EnsurePermission();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MacOSInputSender: Sending Cmd+V paste");
        PostKeyStroke(kVK_V, kCGEventFlagMaskCommand);
    }

    /// <summary>
    /// Simulate Return key press.
    /// </summary>
    public static void SendEnter()
    {
        EnsurePermission();
        PostKeyStroke(kVK_Return, 0);
    }

    /// <summary>
    /// Simulate Backspace key press.
    /// </summary>
    public static void SendBackspace()
    {
        EnsurePermission();
        PostKeyStroke(kVK_Delete, 0);
    }

    /// <summary>
    /// Type text by posting CGEvents with unicode strings.
    /// </summary>
    public static void SendText(string text)
    {
        EnsurePermission();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MacOSInputSender: Sending text ({text.Length} chars)");

        var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);

        // Send in chunks — CGEventKeyboardSetUnicodeString supports up to ~20 chars reliably
        const int chunkSize = 16;
        for (int i = 0; i < text.Length; i += chunkSize)
        {
            var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));

            var keyDown = CGEventCreateKeyboardEvent(source, 0, true);
            var keyUp = CGEventCreateKeyboardEvent(source, 0, false);

            if (keyDown == IntPtr.Zero || keyUp == IntPtr.Zero)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MacOSInputSender: CGEventCreateKeyboardEvent returned null - check Accessibility permission");
                if (keyDown != IntPtr.Zero) CFRelease(keyDown);
                if (keyUp != IntPtr.Zero) CFRelease(keyUp);
                break;
            }

            CGEventKeyboardSetUnicodeString(keyDown, chunk.Length, chunk);
            CGEventKeyboardSetUnicodeString(keyUp, chunk.Length, chunk);

            CGEventPost(kCGSessionEventTap, keyDown);
            CGEventPost(kCGSessionEventTap, keyUp);

            CFRelease(keyDown);
            CFRelease(keyUp);

            // Small delay between chunks to let the system process them
            if (i + chunkSize < text.Length)
                Thread.Sleep(5);
        }

        if (source != IntPtr.Zero)
            CFRelease(source);
    }

    private static void PostKeyStroke(ushort keyCode, ulong modifierFlags)
    {
        var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);

        var keyDown = CGEventCreateKeyboardEvent(source, keyCode, true);
        var keyUp = CGEventCreateKeyboardEvent(source, keyCode, false);

        if (keyDown == IntPtr.Zero || keyUp == IntPtr.Zero)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MacOSInputSender: CGEventCreateKeyboardEvent returned null - check Accessibility permission");
            if (keyDown != IntPtr.Zero) CFRelease(keyDown);
            if (keyUp != IntPtr.Zero) CFRelease(keyUp);
            if (source != IntPtr.Zero) CFRelease(source);
            return;
        }

        if (modifierFlags != 0)
        {
            CGEventSetFlags(keyDown, modifierFlags);
            CGEventSetFlags(keyUp, modifierFlags);
        }

        CGEventPost(kCGSessionEventTap, keyDown);
        Thread.Sleep(50);
        CGEventPost(kCGSessionEventTap, keyUp);

        CFRelease(keyDown);
        CFRelease(keyUp);
        if (source != IntPtr.Zero)
            CFRelease(source);
    }
}
