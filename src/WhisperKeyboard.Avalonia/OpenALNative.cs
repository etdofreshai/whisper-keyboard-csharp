using System.Runtime.InteropServices;

namespace WhisperKeyboard.Avalonia;

/// <summary>
/// Pure P/Invoke wrapper for OpenAL, bypassing OpenTK to directly use openal-soft on macOS.
/// This is needed because OpenTK uses macOS's built-in OpenAL framework which doesn't support capture.
/// </summary>
public static class OpenALNative
{
    // OpenAL-soft library path - try multiple locations
    private static readonly string[] LibraryPaths = new[]
    {
        "/opt/homebrew/opt/openal-soft/lib/libopenal.dylib",  // Apple Silicon Mac
        "/usr/local/opt/openal-soft/lib/libopenal.dylib",     // Intel Mac
        "/usr/lib/libopenal.so.1",                             // Linux
        "/usr/lib/x86_64-linux-gnu/libopenal.so.1",           // Ubuntu/Debian
        "openal32.dll",                                        // Windows
        "libopenal.so",                                        // Generic Linux
    };

    private static IntPtr _libraryHandle;

    // Function delegates
    private delegate IntPtr AlcOpenDeviceDelegate(string? devicename);
    private delegate bool AlcCloseDeviceDelegate(IntPtr device);
    private delegate IntPtr AlcCaptureOpenDeviceDelegate(string? devicename, int frequency, int format, int buffersize);
    private delegate bool AlcCaptureCloseDeviceDelegate(IntPtr device);
    private delegate void AlcCaptureStartDelegate(IntPtr device);
    private delegate void AlcCaptureStopDelegate(IntPtr device);
    private delegate void AlcCaptureSamplesDelegate(IntPtr device, IntPtr buffer, int samples);
    private delegate void AlcGetIntegervDelegate(IntPtr device, int param, int size, out int values);
    private delegate IntPtr AlcGetStringDelegate(IntPtr device, int param);

    // Cached function pointers
    private static AlcOpenDeviceDelegate? _alcOpenDevice;
    private static AlcCloseDeviceDelegate? _alcCloseDevice;
    private static AlcCaptureOpenDeviceDelegate? _alcCaptureOpenDevice;
    private static AlcCaptureCloseDeviceDelegate? _alcCaptureCloseDevice;
    private static AlcCaptureStartDelegate? _alcCaptureStart;
    private static AlcCaptureStopDelegate? _alcCaptureStop;
    private static AlcCaptureSamplesDelegate? _alcCaptureSamples;
    private static AlcGetIntegervDelegate? _alcGetIntegerv;
    private static AlcGetStringDelegate? _alcGetString;

    // OpenAL constants
    public const int ALC_CAPTURE_DEVICE_SPECIFIER = 0x310;
    public const int ALC_CAPTURE_SAMPLES = 0x312;
    public const int AL_FORMAT_MONO16 = 0x1101;

    static OpenALNative()
    {
        LoadLibrary();
    }

    private static void LoadLibrary()
    {
        foreach (var path in LibraryPaths)
        {
            if (NativeLibrary.TryLoad(path, out _libraryHandle))
            {
                Console.WriteLine($"Loaded OpenAL from: {path}");
                LoadFunctions();
                return;
            }
        }

        // Try loading by name as fallback
        if (NativeLibrary.TryLoad("openal", out _libraryHandle) ||
            NativeLibrary.TryLoad("OpenAL", out _libraryHandle))
        {
            Console.WriteLine("Loaded OpenAL from system path");
            LoadFunctions();
            return;
        }

        throw new DllNotFoundException(
            "Could not load OpenAL library. On macOS, install with: brew install openal-soft");
    }

    private static void LoadFunctions()
    {
        _alcOpenDevice = GetFunction<AlcOpenDeviceDelegate>("alcOpenDevice");
        _alcCloseDevice = GetFunction<AlcCloseDeviceDelegate>("alcCloseDevice");
        _alcCaptureOpenDevice = GetFunction<AlcCaptureOpenDeviceDelegate>("alcCaptureOpenDevice");
        _alcCaptureCloseDevice = GetFunction<AlcCaptureCloseDeviceDelegate>("alcCaptureCloseDevice");
        _alcCaptureStart = GetFunction<AlcCaptureStartDelegate>("alcCaptureStart");
        _alcCaptureStop = GetFunction<AlcCaptureStopDelegate>("alcCaptureStop");
        _alcCaptureSamples = GetFunction<AlcCaptureSamplesDelegate>("alcCaptureSamples");
        _alcGetIntegerv = GetFunction<AlcGetIntegervDelegate>("alcGetIntegerv");
        _alcGetString = GetFunction<AlcGetStringDelegate>("alcGetString");
    }

    private static T GetFunction<T>(string name) where T : Delegate
    {
        var ptr = NativeLibrary.GetExport(_libraryHandle, name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // Public API

    public static IntPtr CaptureOpenDevice(string? deviceName, int sampleRate, int format, int bufferSize)
    {
        return _alcCaptureOpenDevice!(deviceName, sampleRate, format, bufferSize);
    }

    public static bool CaptureCloseDevice(IntPtr device)
    {
        return _alcCaptureCloseDevice!(device);
    }

    public static void CaptureStart(IntPtr device)
    {
        _alcCaptureStart!(device);
    }

    public static void CaptureStop(IntPtr device)
    {
        _alcCaptureStop!(device);
    }

    public static void CaptureSamples(IntPtr device, short[] buffer, int samples)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            _alcCaptureSamples!(device, handle.AddrOfPinnedObject(), samples);
        }
        finally
        {
            handle.Free();
        }
    }

    public static int GetCapturedSamples(IntPtr device)
    {
        _alcGetIntegerv!(device, ALC_CAPTURE_SAMPLES, 1, out int samples);
        return samples;
    }

    public static List<string> GetCaptureDeviceNames()
    {
        var devices = new List<string>();
        var ptr = _alcGetString!(IntPtr.Zero, ALC_CAPTURE_DEVICE_SPECIFIER);

        if (ptr == IntPtr.Zero)
            return devices;

        // The string is a null-separated list terminated by double-null
        while (true)
        {
            var str = Marshal.PtrToStringAnsi(ptr);
            if (string.IsNullOrEmpty(str))
                break;

            devices.Add(str);
            ptr = IntPtr.Add(ptr, str.Length + 1);
        }

        return devices;
    }
}
