using System.Runtime.InteropServices;

namespace WhisperKeyboard;

/// <summary>
/// macOS global hotkey implementation using CGEventTap.
/// Requires accessibility permissions to work properly.
/// </summary>
public class GlobalHotkey : IDisposable
{
    // CGEvent types
    private const int kCGEventKeyDown = 10;
    private const int kCGEventFlagsChanged = 12;
    private const int kCGEventNull = 0; // NULL event for tap disabled

    // CGEvent flags (modifier keys)
    private const ulong kCGEventFlagMaskCommand = 0x00100000;
    private const ulong kCGEventFlagMaskShift = 0x00020000;
    private const ulong kCGEventFlagMaskAlternate = 0x00080000;
    private const ulong kCGEventFlagMaskControl = 0x00040000;

    // CGEventTap location
    private const int kCGSessionEventTap = 1;
    private const int kCGHeadInsertEventTap = 0;

    // CFRunLoop constants
    private const string kCFRunLoopCommonModes = "kCFRunLoopCommonModes";
    private const string kCFRunLoopDefaultMode = "kCFRunLoopDefaultMode";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGEventTapCallback(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern IntPtr CGEventTapCreate(
        int tap,
        int place,
        int options,
        ulong eventsOfInterest,
        CGEventTapCallback callback,
        IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern ulong CGEventGetFlags(IntPtr eventRef);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern long CGEventGetIntegerValueField(IntPtr eventRef, int field);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, int order);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetMain();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopRun();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRunLoopStop(IntPtr rl);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, int encoding);

    private readonly List<(uint keyCode, ulong modifiers, Action callback)> _registeredHotkeys = new();
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;
    private IntPtr _runLoop;
    private CGEventTapCallback? _callbackDelegate;
    private Thread? _runLoopThread;
    private bool _disposed;
    private volatile bool _stopRequested;
    private readonly ManualResetEventSlim _eventTapReady = new(false);

    private static readonly Dictionary<string, uint> KeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters
        { "A", 0x00 }, { "B", 0x0B }, { "C", 0x08 }, { "D", 0x02 }, { "E", 0x0E },
        { "F", 0x03 }, { "G", 0x05 }, { "H", 0x04 }, { "I", 0x22 }, { "J", 0x26 },
        { "K", 0x28 }, { "L", 0x25 }, { "M", 0x2E }, { "N", 0x2D }, { "O", 0x1F },
        { "P", 0x23 }, { "Q", 0x0C }, { "R", 0x0F }, { "S", 0x01 }, { "T", 0x11 },
        { "U", 0x20 }, { "V", 0x09 }, { "W", 0x0D }, { "X", 0x07 }, { "Y", 0x10 },
        { "Z", 0x06 },
        // Numbers
        { "0", 0x1D }, { "1", 0x12 }, { "2", 0x13 }, { "3", 0x14 }, { "4", 0x15 },
        { "5", 0x17 }, { "6", 0x16 }, { "7", 0x1A }, { "8", 0x1C }, { "9", 0x19 },
        // Function keys
        { "F1", 0x7A }, { "F2", 0x78 }, { "F3", 0x63 }, { "F4", 0x76 },
        { "F5", 0x60 }, { "F6", 0x61 }, { "F7", 0x62 }, { "F8", 0x64 },
        { "F9", 0x65 }, { "F10", 0x6D }, { "F11", 0x67 }, { "F12", 0x6F },
        // Special keys
        { "Space", 0x31 }, { "Return", 0x24 }, { "Enter", 0x24 }, { "Tab", 0x30 },
        { "Escape", 0x35 }, { "Delete", 0x33 }, { "Backspace", 0x33 },
        { "Up", 0x7E }, { "Down", 0x7D }, { "Left", 0x7B }, { "Right", 0x7C },
        { "Home", 0x73 }, { "End", 0x77 }, { "PageUp", 0x74 }, { "PageDown", 0x79 },
    };

    public GlobalHotkey()
    {
        // Start the event tap on a background thread with its own run loop
        _runLoopThread = new Thread(RunEventTapThread)
        {
            Name = "GlobalHotkeyThread",
            IsBackground = true
        };
        _runLoopThread.Start();
    }

    private void RunEventTapThread()
    {
        try
        {
            // Keep a reference to prevent GC
            _callbackDelegate = EventTapCallback;

            // We only care about key down events
            ulong eventsOfInterest = (1UL << kCGEventKeyDown);

            _eventTap = CGEventTapCreate(
                kCGSessionEventTap,
                kCGHeadInsertEventTap,
                0, // kCGEventTapOptionDefault - we don't want to block events
                eventsOfInterest,
                _callbackDelegate,
                IntPtr.Zero);

            if (_eventTap == IntPtr.Zero)
            {
                Console.WriteLine("Failed to create event tap. Make sure accessibility permissions are granted.");
                Console.WriteLine("Go to System Settings > Privacy & Security > Accessibility and add this app.");
                return;
            }

            _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
            if (_runLoopSource == IntPtr.Zero)
            {
                Console.WriteLine("Failed to create run loop source");
                return;
            }

            // Get the current thread's run loop
            _runLoop = CFRunLoopGetCurrent();

            // Get the default mode string
            var modesPtr = CFStringCreateWithCString(IntPtr.Zero, kCFRunLoopDefaultMode, 0x08000100); // kCFStringEncodingUTF8

            CFRunLoopAddSource(_runLoop, _runLoopSource, modesPtr);
            CGEventTapEnable(_eventTap, true);

            CFRelease(modesPtr);

            Console.WriteLine("Global hotkey event tap installed successfully");

            // Signal that the event tap is ready
            _eventTapReady.Set();

            // Run the loop - this blocks until CFRunLoopStop is called
            while (!_stopRequested)
            {
                CFRunLoopRun();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in hotkey thread: {ex.Message}");
            _eventTapReady.Set(); // Signal anyway so callers don't hang
        }
    }

    private IntPtr EventTapCallback(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo)
    {
        // Handle event tap disabled events (type 0 = kCGEventTapDisabledByTimeout or kCGEventTapDisabledByUserInput)
        if (type == 0)
        {
            Console.WriteLine("Event tap was disabled, re-enabling...");
            if (_eventTap != IntPtr.Zero)
            {
                CGEventTapEnable(_eventTap, true);
            }
            return eventRef;
        }

        if (type != kCGEventKeyDown)
        {
            return eventRef;
        }

        // Get the key code (field 9 = kCGKeyboardEventKeycode)
        var keyCode = (uint)CGEventGetIntegerValueField(eventRef, 9);
        var flags = CGEventGetFlags(eventRef);

        // Check against registered hotkeys
        foreach (var (registeredKeyCode, registeredModifiers, callback) in _registeredHotkeys)
        {
            if (keyCode == registeredKeyCode && ModifiersMatch(flags, registeredModifiers))
            {
                Console.WriteLine($"Hotkey triggered: keyCode=0x{keyCode:X2}");

                // Invoke callback on UI thread
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Hotkey callback error: {ex.Message}");
                    }
                });

                // Don't consume the event - let it pass through
                break;
            }
        }

        return eventRef;
    }

    private bool ModifiersMatch(ulong eventFlags, ulong requiredModifiers)
    {
        // Mask out non-modifier flags
        const ulong modifierMask = kCGEventFlagMaskCommand | kCGEventFlagMaskShift |
                                   kCGEventFlagMaskAlternate | kCGEventFlagMaskControl;

        var eventModifiers = eventFlags & modifierMask;

        // Check if all required modifiers are present
        return (eventModifiers & requiredModifiers) == requiredModifiers &&
               // And no extra modifiers (optional - could be more lenient)
               (eventModifiers & ~requiredModifiers) == 0;
    }

    /// <summary>
    /// Register a global hotkey. Returns true if successful.
    /// </summary>
    public uint Register(string hotkeyString, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            return 0;

        // Wait for the event tap to be ready (with timeout)
        if (!_eventTapReady.Wait(2000)) // Wait up to 2 seconds
        {
            Console.WriteLine("Timeout waiting for event tap - cannot register hotkey");
            return 0;
        }

        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("Event tap not available - cannot register hotkey");
            return 0;
        }

        if (!ParseHotkey(hotkeyString, out uint keyCode, out ulong modifiers))
        {
            Console.WriteLine($"Failed to parse hotkey: {hotkeyString}");
            return 0;
        }

        _registeredHotkeys.Add((keyCode, modifiers, callback));
        Console.WriteLine($"Registered hotkey: {hotkeyString} (keyCode={keyCode:X}, modifiers={modifiers:X})");

        return (uint)_registeredHotkeys.Count;
    }

    /// <summary>
    /// Unregister all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        _registeredHotkeys.Clear();
    }

    private bool ParseHotkey(string hotkeyString, out uint keyCode, out ulong modifiers)
    {
        keyCode = 0;
        modifiers = 0;

        var parts = hotkeyString.Split('+').Select(p => p.Trim()).ToArray();
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            switch (lower)
            {
                case "ctrl":
                case "control":
                    modifiers |= kCGEventFlagMaskControl;
                    break;
                case "alt":
                case "option":
                case "opt":
                    modifiers |= kCGEventFlagMaskAlternate;
                    break;
                case "shift":
                    modifiers |= kCGEventFlagMaskShift;
                    break;
                case "cmd":
                case "command":
                case "win":
                case "meta":
                    modifiers |= kCGEventFlagMaskCommand;
                    break;
                default:
                    // This should be the key
                    if (KeyCodes.TryGetValue(part, out var code))
                    {
                        keyCode = code;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown key: {part}");
                        return false;
                    }
                    break;
            }
        }

        return keyCode != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        UnregisterAll();
        _stopRequested = true;

        // Stop the run loop
        if (_runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(_runLoop);
        }

        // Wait for thread to finish
        if (_runLoopThread != null && _runLoopThread.IsAlive)
        {
            _runLoopThread.Join(1000); // Wait up to 1 second
        }

        if (_runLoopSource != IntPtr.Zero)
        {
            var modesPtr = CFStringCreateWithCString(IntPtr.Zero, kCFRunLoopDefaultMode, 0x08000100);
            if (_runLoop != IntPtr.Zero)
            {
                CFRunLoopRemoveSource(_runLoop, _runLoopSource, modesPtr);
            }
            CFRelease(modesPtr);
            CFRelease(_runLoopSource);
            _runLoopSource = IntPtr.Zero;
        }

        if (_eventTap != IntPtr.Zero)
        {
            CGEventTapEnable(_eventTap, false);
            CFRelease(_eventTap);
            _eventTap = IntPtr.Zero;
        }

        _eventTapReady.Dispose();

        _disposed = true;
    }
}
