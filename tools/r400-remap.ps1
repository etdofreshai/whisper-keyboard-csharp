# Remaps the B button on a Logitech R400 presenter (VID_3151 PID_3020) to F13.
# Filters by HID device so your regular keyboard's B is unaffected.
# Note: the remote's B is also still delivered to the focused app (Raw Input is observational).
# Run with: powershell -ExecutionPolicy Bypass -File r400-remap.ps1
# To run hidden at login, see the .vbs launcher next to this file.

Add-Type -AssemblyName System.Windows.Forms

Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class RemoteWnd : NativeWindow {
    [DllImport("user32.dll")]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, StringBuilder pData, ref uint pcbSize);
    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE {
        public ushort UsagePage;
        public ushort Usage;
        public uint   Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER {
        public uint   dwType;
        public uint   dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint   Message;
        public uint   ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUT {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD    keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public uint       type;
        public KEYBDINPUT ki;
        public uint       pad1;
        public uint       pad2;
    }

    const int   WM_INPUT        = 0x00FF;
    const uint  RIDI_DEVICENAME = 0x20000007;
    const uint  RID_INPUT       = 0x10000003;
    const uint  RIM_TYPEKEYBOARD= 1;
    const ushort VK_B           = 0x42;
    const ushort VK_F13         = 0x7C;
    const uint  INPUT_KEYBOARD  = 1;
    const uint  KEYEVENTF_KEYUP = 0x0002;
    const uint  RIDEV_INPUTSINK = 0x00000100;
    const int   HWND_MESSAGE    = -3;

    public RemoteWnd() {
        var cp = new CreateParams();
        cp.Parent = (IntPtr)HWND_MESSAGE;
        CreateHandle(cp);
        var rid = new RAWINPUTDEVICE[1];
        rid[0].UsagePage = 1;   // Generic Desktop
        rid[0].Usage     = 6;   // Keyboard
        rid[0].Flags     = RIDEV_INPUTSINK;
        rid[0].Target    = Handle;
        if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            throw new Exception("RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error());
        Console.WriteLine("R400 remap active. Listening for VID_3151&PID_3020 keyboard input...");
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_INPUT) {
            uint size = 0;
            uint hsz  = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(m.LParam, RID_INPUT, IntPtr.Zero, ref size, hsz);
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try {
                if (GetRawInputData(m.LParam, RID_INPUT, buf, ref size, hsz) == size) {
                    var ri = (RAWINPUT)Marshal.PtrToStructure(buf, typeof(RAWINPUT));
                    bool isKeyDown = (ri.keyboard.Flags & 1) == 0;
                    if (ri.header.dwType == RIM_TYPEKEYBOARD && ri.keyboard.VKey == VK_B && isKeyDown) {
                        uint nameSz = 0;
                        GetRawInputDeviceInfo(ri.header.hDevice, RIDI_DEVICENAME, null, ref nameSz);
                        var sb = new StringBuilder((int)nameSz + 1);
                        GetRawInputDeviceInfo(ri.header.hDevice, RIDI_DEVICENAME, sb, ref nameSz);
                        string name = sb.ToString();
                        if (name.IndexOf("VID_3151", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            name.IndexOf("PID_3020", StringComparison.OrdinalIgnoreCase) >= 0) {
                            SendF13Press();
                            Console.WriteLine("Remote B tap -> F13 pressed");
                        }
                    }
                }
            } finally { Marshal.FreeHGlobal(buf); }
        }
        base.WndProc(ref m);
    }

    static void SendF13Press() {
        var inputs = new INPUT[2];
        inputs[0].type      = INPUT_KEYBOARD;
        inputs[0].ki.wVk    = VK_F13;
        inputs[1].type      = INPUT_KEYBOARD;
        inputs[1].ki.wVk    = VK_F13;
        inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

$wnd = New-Object RemoteWnd
[System.Windows.Forms.Application]::Run()
