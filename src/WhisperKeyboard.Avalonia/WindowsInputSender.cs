using System.Runtime.InteropServices;

namespace WhisperKeyboard.Avalonia;

public static class WindowsInputSender
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    public static void SendText(string text)
    {
        var inputs = new List<INPUT>();

        foreach (char c in text)
        {
            // Key Down
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });

            // Key Up
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        }
    }

    public static void SendEnter()
    {
        // VK_RETURN = 0x0D
        SendKey(0x0D);
    }

    public static void SendPaste()
    {
        // VK_CONTROL = 0x11, V = 0x56
        var inputs = new List<INPUT>();

        // Ctrl Down
        inputs.Add(MakeKeyInput(0x11, false));
        // V Down
        inputs.Add(MakeKeyInput(0x56, false));
        // V Up
        inputs.Add(MakeKeyInput(0x56, true));
        // Ctrl Up
        inputs.Add(MakeKeyInput(0x11, true));

        SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
    }

    private static void SendKey(ushort vk)
    {
        var inputs = new INPUT[2];
        
        // Down
        inputs[0] = MakeKeyInput(vk, false);
        // Up
        inputs[1] = MakeKeyInput(vk, true);

        SendInput((uint)inputs.Length, inputs, INPUT.Size);
    }

    private static INPUT MakeKeyInput(ushort vk, bool up)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = up ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}
