using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WhisperKeyboard;

public class TextTyper
{
    private readonly Config _config;
    private readonly object _typingLock = new();
    private string _lastTypedText = "";
    private DateTime _lastTypeTime = DateTime.MinValue;

    // Windows API imports for keyboard simulation
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_BACK = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public TextTyper(Config config)
    {
        _config = config;
    }

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync called with: \"{text}\"");

        // Process and clean text
        text = ProcessText(text);

        if (string.IsNullOrEmpty(text)) return;

        // Check for duplicate text (within 2 seconds)
        if (text == _lastTypedText && (DateTime.Now - _lastTypeTime).TotalSeconds < 2)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Skipping duplicate text");
            return;
        }

        _lastTypedText = text;
        _lastTypeTime = DateTime.Now;

        try
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting to type (PasteMode={_config.PasteMode})...");

            lock (_typingLock)
            {
                if (_config.PasteMode)
                {
                    PasteText(text);
                }
                else
                {
                    TypeCharacters(text);
                }

                if (_config.AutoEnter)
                {
                    SendKey(VK_RETURN);
                }
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Typing completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in TypeTextAsync: {ex.Message}");
            throw;
        }

        await Task.CompletedTask;
    }

    private string ProcessText(string text)
    {
        // Clean whitespace
        text = text.Trim();
        text = Regex.Replace(text, @"\s+", " ");

        // Check for "enter" or "over" command at end
        bool shouldEnter = false;
        if (text.EndsWith(" enter", StringComparison.OrdinalIgnoreCase) ||
            text.EndsWith(" over", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^6].Trim();
            shouldEnter = true;
        }

        // Add punctuation if enabled and text doesn't end with punctuation
        if (_config.AddPunctuation && !string.IsNullOrEmpty(text))
        {
            char lastChar = text[^1];
            if (!".!?,:;".Contains(lastChar))
            {
                text += ".";
            }
        }

        // Capitalize first letter if enabled
        if (_config.CapitalizeSentences && !string.IsNullOrEmpty(text))
        {
            text = char.ToUpper(text[0]) + text[1..];

            // Also capitalize after sentence-ending punctuation
            text = Regex.Replace(text, @"([.!?]\s+)([a-z])", m =>
                m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));
        }

        // Add space after punctuation if missing
        text = Regex.Replace(text, @"([.!?,;:])([A-Za-z])", "$1 $2");

        return text;
    }

    private void TypeCharacters(string text)
    {
        foreach (char c in text)
        {
            SendCharacter(c);
            Thread.Sleep((int)(_config.TypingSpeed * 1000));
        }
    }

    private void SendCharacter(char c)
    {
        // Use Unicode input for reliable character entry
        var inputs = new INPUT[2];

        // Key down
        inputs[0].type = 1; // INPUT_KEYBOARD
        inputs[0].u.ki.wVk = 0;
        inputs[0].u.ki.wScan = c;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
        inputs[0].u.ki.time = 0;
        inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

        // Key up
        inputs[1].type = 1;
        inputs[1].u.ki.wVk = 0;
        inputs[1].u.ki.wScan = c;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        inputs[1].u.ki.time = 0;
        inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void PasteText(string text)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] PasteText starting for: \"{text}\"");

        // Clipboard operations must run on STA thread
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            // Store current clipboard content
            string? previousClipboard = null;
            try
            {
                if (Clipboard.ContainsText())
                {
                    previousClipboard = Clipboard.GetText();
                }
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Saved previous clipboard");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to get clipboard: {ex.Message}");
            }

            try
            {
                // Set text to clipboard
                Clipboard.SetText(text);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Set clipboard text");

                // Small delay to ensure clipboard is ready
                Thread.Sleep(50);

                // Send Ctrl+V
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending Ctrl+V...");
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(0x56, 0, 0, UIntPtr.Zero); // V key
                keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ctrl+V sent");

                // Small delay after paste
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] PasteText inner error: {ex.Message}");
                threadException = ex;
            }
            finally
            {
                // Restore previous clipboard content
                try
                {
                    if (previousClipboard != null)
                    {
                        Thread.Sleep(100);
                        Clipboard.SetText(previousClipboard);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Restored previous clipboard");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Failed to restore clipboard: {ex.Message}");
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(); // Wait for completion

        if (threadException != null)
        {
            throw threadException;
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] PasteText completed");
    }

    public void SendBackspace(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            SendKey(VK_BACK);
            Thread.Sleep(20);
        }
    }

    public void SendEnter()
    {
        SendKey(VK_RETURN);
    }
}
