using WhisperKeyboard.Core;

namespace WhisperKeyboard;

/// <summary>
/// Cross-platform audio capture using OpenAL via pure P/Invoke.
/// Uses openal-soft on macOS/Linux for capture support.
/// </summary>
public class OpenALAudioCapture : IAudioCapture
{
    private readonly Config _config;
    private IntPtr _captureDevice;
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
    private TimeSpan _accumulatedSpeechDuration;

    public event EventHandler<AudioReadyEventArgs>? AudioReady;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? SpeechDetected;

    public bool IsRecording => _isRunning;
    public bool IsPaused => _isPaused;
    public bool IsSpeechDetected => _isSpeechDetected;

    public OpenALAudioCapture(Config config)
    {
        _config = config;
    }

    public List<string> GetAudioDevices()
    {
        try
        {
            return OpenALNative.GetCaptureDeviceNames();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating audio devices: {ex.Message}");
            return new List<string>();
        }
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
            _captureDevice = OpenALNative.CaptureOpenDevice(
                deviceName,
                _config.SampleRate,
                OpenALNative.AL_FORMAT_MONO16,
                bufferSize
            );

            if (_captureDevice == IntPtr.Zero)
            {
                throw new Exception("Failed to open audio capture device. Make sure a microphone is connected and permissions are granted.");
            }

            OpenALNative.CaptureStart(_captureDevice);
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

        if (_captureDevice != IntPtr.Zero)
        {
            OpenALNative.CaptureStop(_captureDevice);
            OpenALNative.CaptureCloseDevice(_captureDevice);
            _captureDevice = IntPtr.Zero;
        }

        Console.WriteLine("Audio capture stopped");
    }

    public void Pause()
    {
        _isPaused = true;
        if (_captureDevice != IntPtr.Zero)
        {
            OpenALNative.CaptureStop(_captureDevice);
        }
    }

    public void Resume()
    {
        if (!_isPaused) return;

        // Reset timing so silence duration doesn't include pause time
        if (_isSpeechDetected)
        {
            _lastSpeechTime = DateTime.Now;
        }

        if (_captureDevice != IntPtr.Zero)
        {
            OpenALNative.CaptureStart(_captureDevice);
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
                // Check how many samples are available
                int samplesAvailable = OpenALNative.GetCapturedSamples(_captureDevice);

                if (samplesAvailable >= samplesPerBuffer)
                {
                    // Capture samples
                    OpenALNative.CaptureSamples(_captureDevice, buffer, samplesPerBuffer);

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

    private void ProcessAudioData(byte[] buffer, int bytesRecorded)
    {
        // Calculate RMS volume
        double normalizedVolume = AudioProcessingUtils.GetNormalizedVolume(buffer, bytesRecorded);
        VolumeChanged?.Invoke(this, normalizedVolume);

        // Voice Activity Detection
        bool isSpeech = normalizedVolume > _config.VadThreshold;

        lock (_bufferLock)
        {
            var chunk = new byte[bytesRecorded];
            Array.Copy(buffer, chunk, bytesRecorded);

            if (isSpeech)
            {
                if (!_isSpeechDetected)
                {
                    // Speech just started
                    _isSpeechDetected = true;
                    _speechStartTime = DateTime.Now;
                    
                    // Calculate duration of voice buffer (pre-roll)
                    long bufferBytes = _voiceBuffer.Sum(b => b.Length);
                    double bufferDuration = (double)bufferBytes / (_config.SampleRate * 2);
                    _accumulatedSpeechDuration = TimeSpan.FromSeconds(bufferDuration);
                    
                    SpeechDetected?.Invoke(this, true);

                    // Add context from voice buffer
                    _audioBuffer.AddRange(_voiceBuffer);
                    _voiceBuffer.Clear();
                }

                // Calculate chunk duration
                double chunkDuration = (double)bytesRecorded / (_config.SampleRate * 2); // 16-bit = 2 bytes
                _accumulatedSpeechDuration += TimeSpan.FromSeconds(chunkDuration);

                _lastSpeechTime = DateTime.Now;
                _audioBuffer.Add(chunk);
            }
            else 
            {
                // Silence - maintain pre-roll buffer
                _voiceBuffer.Add(chunk);
                while (_voiceBuffer.Count > 5)
                {
                    _voiceBuffer.RemoveAt(0);
                }

                if (_isSpeechDetected)
                {
                    // Still recording after speech (trailing silence)
                    _audioBuffer.Add(chunk);

                    var silenceDuration = (DateTime.Now - _lastSpeechTime).TotalSeconds;

                    if (silenceDuration > _config.MaxSilenceDuration)
                    {
                        // End of speech detected
                        FinalizeAudio();
                    }
                }
            }
        }
    }

    private void FinalizeAudio()
    {
        // Calculate duration from first speech to last speech (excluding trailing silence)
        // This is the "speech span" - from when speech started to when it last ended
        var speechSpanDuration = _lastSpeechTime - _speechStartTime;
        var speechDuration = _accumulatedSpeechDuration;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Finalizing audio. Speech Span: {speechSpanDuration.TotalSeconds:F2}s, Active Speech: {speechDuration.TotalSeconds:F2}s");

        // Always send audio - let the application layer decide based on transcription result
        long totalBytes = _audioBuffer.Sum(b => b.Length);
        var combinedAudio = new byte[totalBytes];
        int offset = 0;

        foreach (var chunk in _audioBuffer)
        {
            Array.Copy(chunk, 0, combinedAudio, offset, chunk.Length);
            offset += chunk.Length;
        }

        AudioReady?.Invoke(this, new AudioReadyEventArgs(combinedAudio, speechSpanDuration, speechDuration));

        // Reset state
        _audioBuffer.Clear();
        _accumulatedSpeechDuration = TimeSpan.Zero;
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
