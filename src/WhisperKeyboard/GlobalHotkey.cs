using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WhisperKeyboard;

/// <summary>
/// Cross-platform global hotkey implementation.
/// Uses Win32 RegisterHotKey on Windows, CGEventTap on macOS.
/// </summary>
public class GlobalHotkey : IDisposable
{
    private readonly IGlobalHotkeyImpl _impl;

    public GlobalHotkey()
    {
        if (OperatingSystem.IsWindows())
        {
            _impl = new WindowsGlobalHotkey();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _impl = new MacOSGlobalHotkey();
        }
        else
        {
            _impl = new NullGlobalHotkey();
        }
    }

    public uint Register(string hotkeyString, Action callback)
    {
        return _impl.Register(hotkeyString, callback);
    }

    public void UnregisterAll()
    {
        _impl.UnregisterAll();
    }

    public void Dispose()
    {
        _impl.Dispose();
    }
}

internal interface IGlobalHotkeyImpl : IDisposable
{
    uint Register(string hotkeyString, Action callback);
    void UnregisterAll();
}

/// <summary>
/// Null implementation for unsupported platforms.
/// </summary>
internal class NullGlobalHotkey : IGlobalHotkeyImpl
{
    public uint Register(string hotkeyString, Action callback)
    {
        Console.WriteLine($"Global hotkeys not supported on this platform");
        return 0;
    }

    public void UnregisterAll() { }
    public void Dispose() { }
}

/// <summary>
/// Windows global hotkey implementation using Win32 RegisterHotKey.
/// </summary>
internal class WindowsGlobalHotkey : IGlobalHotkeyImpl
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    // Modifier key constants
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // Message constants
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_USER = 0x0400;
    private const uint WM_REGISTER_HOTKEY = WM_USER + 1;
    private const uint PM_REMOVE = 0x0001;

    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly ConcurrentQueue<(int id, uint modifiers, uint vk, Action callback, TaskCompletionSource<bool> tcs)> _pendingRegistrations = new();
    private int _nextId = 1;
    private IntPtr _hwnd;
    private uint _messageThreadId;
    private Thread? _messageThread;
    private volatile bool _disposed;
    private readonly ManualResetEventSlim _windowReady = new(false);

    // Virtual key codes
    private static readonly Dictionary<string, uint> VirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letters
        { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
        { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
        { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
        { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
        { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
        { "Z", 0x5A },
        // Numbers
        { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
        { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
        // Function keys
        { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
        { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
        { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },
        // Special keys
        { "Space", 0x20 }, { "Return", 0x0D }, { "Enter", 0x0D }, { "Tab", 0x09 },
        { "Escape", 0x1B }, { "Delete", 0x2E }, { "Backspace", 0x08 },
        { "Up", 0x26 }, { "Down", 0x28 }, { "Left", 0x25 }, { "Right", 0x27 },
        { "Home", 0x24 }, { "End", 0x23 }, { "PageUp", 0x21 }, { "PageDown", 0x22 },
    };

    public WindowsGlobalHotkey()
    {
        _messageThread = new Thread(MessageLoop)
        {
            Name = "GlobalHotkeyThread",
            IsBackground = true
        };
        _messageThread.Start();

        // Wait for window to be created
        if (!_windowReady.Wait(5000))
        {
            Console.WriteLine("Timeout waiting for hotkey window to be created");
        }
    }

    private void MessageLoop()
    {
        try
        {
            _messageThreadId = GetCurrentThreadId();

            // Create a message-only window to receive hotkey messages
            _hwnd = CreateWindowEx(
                0, "STATIC", "WhisperKeyboard Hotkey Window",
                0, 0, 0, 0, 0,
                new IntPtr(-3), // HWND_MESSAGE - message-only window
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to create hotkey window. Error: {Marshal.GetLastWin32Error()}");
                _windowReady.Set();
                return;
            }

            Console.WriteLine("Global hotkey window created successfully");
            _windowReady.Set();

            // Message loop
            while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY)
                {
                    int id = (int)msg.wParam;
                    if (_callbacks.TryGetValue(id, out var callback))
                    {
                        Console.WriteLine($"Hotkey triggered: id={id}");
                        // Invoke on UI thread
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
                    }
                }
                else if (msg.message == WM_REGISTER_HOTKEY)
                {
                    // Process pending registrations from the message thread
                    ProcessPendingRegistrations();
                }
                else
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in hotkey message loop: {ex.Message}");
            _windowReady.Set();
        }
    }

    private void ProcessPendingRegistrations()
    {
        while (_pendingRegistrations.TryDequeue(out var reg))
        {
            bool success = RegisterHotKey(_hwnd, reg.id, reg.modifiers | MOD_NOREPEAT, reg.vk);
            if (success)
            {
                _callbacks[reg.id] = reg.callback;
                Console.WriteLine($"Registered hotkey: id={reg.id}, modifiers=0x{reg.modifiers:X}, vk=0x{reg.vk:X}");
            }
            else
            {
                Console.WriteLine($"Failed to register hotkey: id={reg.id}. Error: {Marshal.GetLastWin32Error()}");
            }
            reg.tcs.SetResult(success);
        }
    }

    public uint Register(string hotkeyString, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            return 0;

        if (!_windowReady.Wait(2000))
        {
            Console.WriteLine("Hotkey window not ready - cannot register hotkey");
            return 0;
        }

        if (_hwnd == IntPtr.Zero)
        {
            Console.WriteLine("Hotkey window not available - cannot register hotkey");
            return 0;
        }

        if (!ParseHotkey(hotkeyString, out uint modifiers, out uint vk))
        {
            Console.WriteLine($"Failed to parse hotkey: {hotkeyString}");
            return 0;
        }

        int id = _nextId++;

        // Queue the registration and post a message to process it on the message thread
        var tcs = new TaskCompletionSource<bool>();
        _pendingRegistrations.Enqueue((id, modifiers, vk, callback, tcs));
        PostThreadMessage(_messageThreadId, WM_REGISTER_HOTKEY, IntPtr.Zero, IntPtr.Zero);

        // Wait for the registration to complete (with timeout)
        if (tcs.Task.Wait(2000) && tcs.Task.Result)
        {
            Console.WriteLine($"Registered hotkey: {hotkeyString} (id={id}, modifiers=0x{modifiers:X}, vk=0x{vk:X})");
            return (uint)id;
        }
        else
        {
            Console.WriteLine($"Failed to register hotkey: {hotkeyString}");
            return 0;
        }
    }

    public void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (var id in _callbacks.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _callbacks.Clear();
    }

    private bool ParseHotkey(string hotkeyString, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

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
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                case "option":
                case "opt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                case "cmd":
                case "command":
                case "meta":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    // This should be the key
                    if (VirtualKeys.TryGetValue(part, out var code))
                    {
                        vk = code;
                    }
                    else
                    {
                        Console.WriteLine($"Unknown key: {part}");
                        return false;
                    }
                    break;
            }
        }

        return vk != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAll();

        if (_messageThreadId != 0)
        {
            // Post WM_QUIT to exit the message loop
            PostThreadMessage(_messageThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _messageThread?.Join(1000);
        _windowReady.Dispose();
    }
}

/// <summary>
/// macOS global hotkey implementation using CGEventTap.
/// Requires accessibility permissions to work properly.
/// </summary>
internal class MacOSGlobalHotkey : IGlobalHotkeyImpl
{
    // CGEvent types
    private const int kCGEventKeyDown = 10;
    private const int kCGEventFlagsChanged = 12;
    private const int kCGEventNull = 0;

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

    public MacOSGlobalHotkey()
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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

/// <summary>
/// Push-to-Talk hook using WH_KEYBOARD_LL to detect Right Ctrl + Right Shift hold/release.
/// Windows-only. Fires Started when both keys are held, Stopped when either is released.
/// </summary>
public class PushToTalkHook : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;

    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_RSHIFT = 0xA1;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _rCtrlDown;
    private bool _rShiftDown;
    private bool _isActive;
    private bool _disposed;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private readonly ManualResetEventSlim _hookReady = new(false);

    public event Action? Started;
    public event Action? Stopped;

    public PushToTalkHook()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Push-to-Talk hook is only supported on Windows");
            _hookReady.Set();
            return;
        }

        _hookThread = new Thread(HookThreadProc)
        {
            Name = "PushToTalkHookThread",
            IsBackground = true
        };
        _hookThread.Start();

        if (!_hookReady.Wait(5000))
        {
            Console.WriteLine("Timeout waiting for PTT hook to be installed");
        }
    }

    private void HookThreadProc()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            _proc = HookCallback;

            var moduleHandle = GetModuleHandle(null);
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

            if (_hookId == IntPtr.Zero)
            {
                Console.WriteLine($"Failed to install PTT keyboard hook. Error: {Marshal.GetLastWin32Error()}");
                _hookReady.Set();
                return;
            }

            Console.WriteLine("Push-to-Talk keyboard hook installed (Right Ctrl + Right Shift)");
            _hookReady.Set();

            // Message pump - required for WH_KEYBOARD_LL to work
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_QUIT) break;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in PTT hook thread: {ex.Message}");
            _hookReady.Set();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (hookStruct.vkCode == VK_RCONTROL)
            {
                if (isDown) _rCtrlDown = true;
                else if (isUp) _rCtrlDown = false;
            }
            else if (hookStruct.vkCode == VK_RSHIFT)
            {
                if (isDown) _rShiftDown = true;
                else if (isUp) _rShiftDown = false;
            }

            bool bothDown = _rCtrlDown && _rShiftDown;

            if (bothDown && !_isActive)
            {
                _isActive = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { Started?.Invoke(); }
                    catch (Exception ex) { Console.WriteLine($"PTT Started callback error: {ex.Message}"); }
                });
            }
            else if (!bothDown && _isActive)
            {
                _isActive = false;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { Stopped?.Invoke(); }
                    catch (Exception ex) { Console.WriteLine($"PTT Stopped callback error: {ex.Message}"); }
                });
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _hookThread?.Join(1000);
        _hookReady.Dispose();
    }
}
