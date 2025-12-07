using System.Runtime.InteropServices;

namespace WhisperKeyboard.Avalonia;

/// <summary>
/// macOS global hotkey implementation using Carbon Event Manager.
/// Requires accessibility permissions to work properly.
/// </summary>
public class GlobalHotkey : IDisposable
{
    // Carbon Event Manager constants
    private const uint kEventHotKeyPressed = 5;
    private const uint kEventClassKeyboard = 0x6b657962; // 'keyb'
    private const uint typeEventHotKeyID = 0x686b6964; // 'hkid'

    // Carbon modifier flags
    private const uint cmdKey = 0x0100;
    private const uint shiftKey = 0x0200;
    private const uint optionKey = 0x0800;
    private const uint controlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    // Carbon API imports
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(IntPtr inHotKey);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(
        IntPtr inTarget,
        IntPtr inHandler,
        uint inNumTypes,
        EventTypeSpec[] inList,
        IntPtr inUserData,
        out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RemoveEventHandler(IntPtr inHandlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        out EventHotKeyID outData);

    private delegate int EventHandlerDelegate(IntPtr inHandlerCallRef, IntPtr inEvent, IntPtr inUserData);

    private readonly Dictionary<uint, (IntPtr hotkeyRef, Action callback)> _registeredHotkeys = new();
    private uint _nextId = 1;
    private IntPtr _eventHandlerRef;
    private EventHandlerDelegate? _handlerDelegate;
    private bool _disposed;

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
        InstallHandler();
    }

    private void InstallHandler()
    {
        _handlerDelegate = HotkeyHandler;
        var handlerPtr = Marshal.GetFunctionPointerForDelegate(_handlerDelegate);

        var eventTypes = new EventTypeSpec[]
        {
            new() { eventClass = kEventClassKeyboard, eventKind = kEventHotKeyPressed }
        };

        var result = InstallEventHandler(
            GetApplicationEventTarget(),
            handlerPtr,
            1,
            eventTypes,
            IntPtr.Zero,
            out _eventHandlerRef);

        if (result != 0)
        {
            Console.WriteLine($"Failed to install event handler: {result}");
        }
    }

    private int HotkeyHandler(IntPtr inHandlerCallRef, IntPtr inEvent, IntPtr inUserData)
    {
        var result = GetEventParameter(
            inEvent,
            typeEventHotKeyID,
            typeEventHotKeyID,
            IntPtr.Zero,
            (uint)Marshal.SizeOf<EventHotKeyID>(),
            IntPtr.Zero,
            out EventHotKeyID hotkeyId);

        if (result == 0 && _registeredHotkeys.TryGetValue(hotkeyId.id, out var entry))
        {
            // Invoke callback on main thread
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => entry.callback());
        }

        return 0; // noErr
    }

    /// <summary>
    /// Register a global hotkey. Returns the hotkey ID, or 0 if registration failed.
    /// </summary>
    public uint Register(string hotkeyString, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            return 0;

        if (!ParseHotkey(hotkeyString, out uint keyCode, out uint modifiers))
        {
            Console.WriteLine($"Failed to parse hotkey: {hotkeyString}");
            return 0;
        }

        var id = _nextId++;
        var hotkeyId = new EventHotKeyID { signature = 0x574B4259, id = id }; // 'WKBY' signature

        var result = RegisterEventHotKey(
            keyCode,
            modifiers,
            hotkeyId,
            GetApplicationEventTarget(),
            0,
            out IntPtr hotkeyRef);

        if (result != 0)
        {
            Console.WriteLine($"Failed to register hotkey '{hotkeyString}': error {result}");
            return 0;
        }

        _registeredHotkeys[id] = (hotkeyRef, callback);
        Console.WriteLine($"Registered hotkey: {hotkeyString} (id={id})");
        return id;
    }

    /// <summary>
    /// Unregister a hotkey by its ID.
    /// </summary>
    public void Unregister(uint id)
    {
        if (_registeredHotkeys.TryGetValue(id, out var entry))
        {
            UnregisterEventHotKey(entry.hotkeyRef);
            _registeredHotkeys.Remove(id);
            Console.WriteLine($"Unregistered hotkey id={id}");
        }
    }

    /// <summary>
    /// Unregister all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var kvp in _registeredHotkeys)
        {
            UnregisterEventHotKey(kvp.Value.hotkeyRef);
        }
        _registeredHotkeys.Clear();
    }

    private bool ParseHotkey(string hotkeyString, out uint keyCode, out uint modifiers)
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
                    modifiers |= controlKey;
                    break;
                case "alt":
                case "option":
                case "opt":
                    modifiers |= optionKey;
                    break;
                case "shift":
                    modifiers |= shiftKey;
                    break;
                case "cmd":
                case "command":
                case "win":
                case "meta":
                    modifiers |= cmdKey;
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

        return keyCode != 0 || modifiers != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        UnregisterAll();

        if (_eventHandlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(_eventHandlerRef);
            _eventHandlerRef = IntPtr.Zero;
        }

        _disposed = true;
    }
}
