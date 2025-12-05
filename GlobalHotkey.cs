using System.Runtime.InteropServices;

namespace WhisperKeyboard;

public class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private int _currentId;
    private bool _disposed;

    public GlobalHotkey(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public int Register(string hotkeyString, Action callback)
    {
        var (modifiers, key) = ParseHotkeyString(hotkeyString);

        if (key == 0)
        {
            Console.WriteLine($"Invalid hotkey: {hotkeyString}");
            return -1;
        }

        int id = ++_currentId;

        if (RegisterHotKey(_windowHandle, id, modifiers, key))
        {
            _hotkeys[id] = callback;
            Console.WriteLine($"Registered hotkey: {hotkeyString} (ID: {id})");
            return id;
        }

        Console.WriteLine($"Failed to register hotkey: {hotkeyString}");
        return -1;
    }

    public void Unregister(int id)
    {
        if (_hotkeys.ContainsKey(id))
        {
            UnregisterHotKey(_windowHandle, id);
            _hotkeys.Remove(id);
        }
    }

    public void HandleHotkey(int id)
    {
        if (_hotkeys.TryGetValue(id, out var callback))
        {
            callback.Invoke();
        }
    }

    private static (uint modifiers, uint key) ParseHotkeyString(string hotkeyString)
    {
        uint modifiers = 0;
        uint key = 0;

        var parts = hotkeyString.Split('+');

        foreach (var part in parts)
        {
            var trimmed = part.Trim().ToLower();

            switch (trimmed)
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    key = GetVirtualKeyCode(trimmed);
                    break;
            }
        }

        return (modifiers, key);
    }

    private static uint GetVirtualKeyCode(string key)
    {
        // Single character keys
        if (key.Length == 1)
        {
            char c = char.ToUpper(key[0]);
            if (c >= 'A' && c <= 'Z')
            {
                return (uint)c;
            }
            if (c >= '0' && c <= '9')
            {
                return (uint)c;
            }
        }

        // Function keys
        if (key.StartsWith("f") && int.TryParse(key[1..], out int fNum) && fNum >= 1 && fNum <= 24)
        {
            return (uint)(0x70 + fNum - 1); // VK_F1 = 0x70
        }

        // Special keys
        return key.ToLower() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "printscreen" or "prtsc" => 0x2C,
            "pause" => 0x13,
            "numlock" => 0x90,
            "scrolllock" => 0x91,
            _ => 0
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var id in _hotkeys.Keys.ToList())
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotkeys.Clear();

        _disposed = true;
    }
}
