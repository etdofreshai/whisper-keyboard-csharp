using OpenTK.Audio.OpenAL;
using WhisperKeyboard.Core;
using System.Runtime.InteropServices;

namespace WhisperKeyboard.Avalonia;

/// <summary>
/// Cross-platform audio capture using OpenAL.
/// Works on Windows, macOS, and Linux.
/// </summary>
public class OpenALAudioCapture : IAudioCapture
{
    private readonly Config _config;
    private ALCaptureDevice _captureDevice;
    private Thread? _captureThread;
    private volatile bool _isRunning;
    private volatile bool _isPaused;
    private bool _disposed;

    private readonly List<byte[]> _audioBuffer = new();
    private readonly List<byte[]> _voiceBuffer = new();
    private readonly object _bufferLock = new();
    private bool _isSpeechDetected;
    private DateTime _lastSpeechTime;
    private DateTime _speechStartTime;

    public event EventHandler<byte[]>? AudioReady;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? SpeechDetected;

    public bool IsRecording => _isRunning;
    public bool IsPaused => _isPaused;

    public OpenALAudioCapture(Config config)
    {
        _config = config;
    }

    public List<string> GetAudioDevices()
    {
        var devices = new List<string>();
        try
        {
            var deviceList = ALC.GetString(ALDevice.Null, AlcGetStringList.CaptureDeviceSpecifier);
            if (deviceList != null)
            {
                devices.AddRange(deviceList);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating audio devices: {ex.Message}");
        }
        return devices;
    }

    public void Start()
    {
        if (_isRunning) return;

        try
        {
            // Get device name (null = default)
            string? deviceName = null;
            if (_config.DeviceIndex >= 0)
            {
                var devices = GetAudioDevices();
                if (_config.DeviceIndex < devices.Count)
                {
                    deviceName = devices[_config.DeviceIndex];
                }
            }

            // Open capture device
            // Format: 16-bit mono PCM
            int bufferSize = _config.SampleRate; // 1 second buffer
            _captureDevice = ALC.CaptureOpenDevice(
                deviceName,
                _config.SampleRate,
                ALFormat.Mono16,
                bufferSize
            );

            if (_captureDevice == ALCaptureDevice.Null)
            {
                throw new Exception("Failed to open audio capture device. Make sure a microphone is connected and permissions are granted.");
            }

            ALC.CaptureStart(_captureDevice);
            _isRunning = true;
            _isPaused = false;

            // Start capture thread
            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "OpenAL Capture"
            };
            _captureThread.Start();

            Console.WriteLine($"Audio capture started (device: {deviceName ?? "default"})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio capture: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _captureThread?.Join(1000);

        if (_captureDevice != ALCaptureDevice.Null)
        {
            ALC.CaptureStop(_captureDevice);
            ALC.CaptureCloseDevice(_captureDevice);
            _captureDevice = ALCaptureDevice.Null;
        }

        Console.WriteLine("Audio capture stopped");
    }

    public void Pause()
    {
        _isPaused = true;
        if (_captureDevice != ALCaptureDevice.Null)
        {
            ALC.CaptureStop(_captureDevice);
        }
    }

    public void Resume()
    {
        if (!_isPaused) return;

        if (_captureDevice != ALCaptureDevice.Null)
        {
            ALC.CaptureStart(_captureDevice);
        }
        _isPaused = false;
    }

    private void CaptureLoop()
    {
        // Buffer for ~50ms of audio at 16kHz mono 16-bit = 800 samples
        int samplesPerBuffer = _config.SampleRate / 20; // 50ms worth
        var buffer = new short[samplesPerBuffer];
        var byteBuffer = new byte[samplesPerBuffer * 2]; // 16-bit = 2 bytes per sample

        while (_isRunning)
        {
            if (_isPaused)
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                // Check how many samples are available using the capture device specific call
                int samplesAvailable = GetCaptureSamples(_captureDevice);

                if (samplesAvailable >= samplesPerBuffer)
                {
                    // Capture samples
                    ALC.CaptureSamples(_captureDevice, buffer, samplesPerBuffer);

                    // Convert to bytes
                    Buffer.BlockCopy(buffer, 0, byteBuffer, 0, byteBuffer.Length);

                    // Process the audio data
                    ProcessAudioData(byteBuffer, byteBuffer.Length);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Capture error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Get the number of captured samples available.
    /// OpenTK's ALC.GetInteger requires ALDevice, but for capture we need a different approach.
    /// </summary>
    private static int GetCaptureSamples(ALCaptureDevice device)
    {
        // Use P/Invoke to call alcGetIntegerv directly for capture device
        unsafe
        {
            int samples = 0;
            AlcGetIntegerv(device.Handle, (int)AlcGetInteger.CaptureSamples, 1, &samples);
            return samples;
        }
    }

    // Use the correct library name for each platform
    private const string OpenALLib = "/opt/homebrew/opt/openal-soft/lib/libopenal.dylib";

    [DllImport(OpenALLib, EntryPoint = "alcGetIntegerv", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void AlcGetIntegerv(IntPtr device, int param, int size, int* values);

    private void ProcessAudioData(byte[] buffer, int bytesRecorded)
    {
        // Calculate RMS volume
        double normalizedVolume = AudioProcessingUtils.GetNormalizedVolume(buffer, bytesRecorded);
        VolumeChanged?.Invoke(this, normalizedVolume);

        // Voice Activity Detection
        bool isSpeech = normalizedVolume > _config.VadThreshold;

        lock (_bufferLock)
        {
            // Maintain voice buffer (last few chunks before speech)
            var chunk = new byte[bytesRecorded];
            Array.Copy(buffer, chunk, bytesRecorded);
            _voiceBuffer.Add(chunk);

            // Keep only last 5 chunks in voice buffer
            while (_voiceBuffer.Count > 5)
            {
                _voiceBuffer.RemoveAt(0);
            }

            if (isSpeech)
            {
                if (!_isSpeechDetected)
                {
                    // Speech just started
                    _isSpeechDetected = true;
                    _speechStartTime = DateTime.Now;
                    SpeechDetected?.Invoke(this, true);

                    // Add context from voice buffer
                    _audioBuffer.AddRange(_voiceBuffer);
                    _voiceBuffer.Clear();
                }

                _lastSpeechTime = DateTime.Now;
                _audioBuffer.Add(chunk);
            }
            else if (_isSpeechDetected)
            {
                // Still recording after speech
                _audioBuffer.Add(chunk);

                var silenceDuration = (DateTime.Now - _lastSpeechTime).TotalSeconds;
                var speechDuration = (DateTime.Now - _speechStartTime).TotalSeconds;

                if (silenceDuration > _config.MaxSilenceDuration && speechDuration > _config.MinSpeechDuration)
                {
                    // End of speech detected
                    FinalizeAudio();
                }
            }
        }
    }

    private void FinalizeAudio()
    {
        var totalBytes = _audioBuffer.Sum(b => b.Length);
        var totalDuration = totalBytes / (double)(_config.SampleRate * 2); // 16-bit = 2 bytes per sample

        if (totalDuration >= _config.MinAudioDuration)
        {
            // Combine all audio chunks
            var combinedAudio = new byte[totalBytes];
            int offset = 0;

            foreach (var chunk in _audioBuffer)
            {
                Array.Copy(chunk, 0, combinedAudio, offset, chunk.Length);
                offset += chunk.Length;
            }

            AudioReady?.Invoke(this, combinedAudio);
        }

        // Reset state
        _audioBuffer.Clear();
        _isSpeechDetected = false;
        SpeechDetected?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _disposed = true;
    }
}
