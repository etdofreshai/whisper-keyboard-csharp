using NAudio.Wave;
using System.Collections.Concurrent;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

public class NAudioCapture : IAudioCapture
{
    private readonly Config _config;
    private WaveInEvent? _waveIn;
    private readonly List<byte[]> _audioBuffer = new();
    private readonly List<byte[]> _voiceBuffer = new();
    private readonly object _bufferLock = new();
    private bool _isRecording;
    private bool _isSpeechDetected;
    private DateTime _lastSpeechTime;
    private DateTime _speechStartTime;
    private TimeSpan _accumulatedSpeechDuration;
    private bool _disposed;
    private DateTime _lastVolumeLog = DateTime.MinValue;
    private double _maxVolumeSeen;
    private bool _isLongRecording;
    private DateTime _longRecordingStartTime;
    private volatile bool _isMonitoring;

    public event EventHandler<AudioReadyEventArgs>? AudioReady;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? SpeechDetected;

    public bool IsRecording => _isRecording;
    public bool IsPaused { get; private set; }
    public bool IsSpeechDetected => _isSpeechDetected;
    public bool IsLongRecording => _isLongRecording;
    public bool IsMonitoring => _isMonitoring;

    public NAudioCapture(Config config)
    {
        _config = config;
    }

    public List<string> GetAudioDevices()
    {
        var devices = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var capabilities = WaveInEvent.GetCapabilities(i);
            devices.Add(capabilities.ProductName);
        }
        return devices;
    }

    private void StartDevice()
    {
        _waveIn = new WaveInEvent
        {
            DeviceNumber = _config.DeviceIndex >= 0 ? _config.DeviceIndex : 0,
            WaveFormat = new WaveFormat(_config.SampleRate, 16, _config.Channels),
            BufferMilliseconds = 50
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        _waveIn.StartRecording();
        _isRecording = true;
        IsPaused = false;
    }

    private void StopDevice()
    {
        _isRecording = false;
        _isMonitoring = false;
        _waveIn?.StopRecording();
    }

    /// <summary>
    /// Ensures the capture device is actively recording. The device can be left
    /// paused (e.g. stopped mid-transcription), so monitor/full transitions must
    /// resume it or no audio is captured.
    /// </summary>
    private void EnsureDeviceCapturing()
    {
        if (!IsPaused) return;
        try
        {
            _waveIn?.StartRecording();
            IsPaused = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resuming audio device: {ex.Message}");
        }
    }

    public void Start()
    {
        // Already running (possibly in monitor mode) — just upgrade to full capture.
        if (_isRecording)
        {
            _isMonitoring = false;
            EnsureDeviceCapturing();
            return;
        }

        try
        {
            StartDevice();
            _isMonitoring = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio recording: {ex.Message}");
            throw;
        }
    }

    public void StartMonitoring()
    {
        if (_isRecording) return;

        try
        {
            StartDevice();
            _isMonitoring = true;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Audio monitoring started (mic warm for pre-roll)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio monitoring: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isRecording) return;

        // With pre-roll enabled, keep the mic running in monitor-only mode so the
        // pre-roll buffer stays warm for the next push-to-talk.
        if (_config.PreRollEnabled)
        {
            _isMonitoring = true;
            _isLongRecording = false;
            _isSpeechDetected = false;
            lock (_bufferLock)
            {
                _audioBuffer.Clear();
            }
            // Device may be paused (stopped mid-transcription) — resume it so the
            // pre-roll buffer actually keeps filling.
            EnsureDeviceCapturing();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Audio downgraded to monitor mode");
            return;
        }

        StopDevice();
    }

    public void Pause()
    {
        IsPaused = true;
        _waveIn?.StopRecording();
    }

    public void Resume()
    {
        if (!IsPaused) return;

        // Reset timing so silence duration doesn't include pause time
        if (_isSpeechDetected)
        {
            _lastSpeechTime = DateTime.Now;
        }

        try
        {
            _waveIn?.StartRecording();
            IsPaused = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resuming audio recording: {ex.Message}");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsPaused) return;

        // Calculate RMS volume
        double rms = CalculateRms(e.Buffer, e.BytesRecorded);
        double normalizedVolume = rms * 32768;

        VolumeChanged?.Invoke(this, normalizedVolume);

        // Track max volume for debugging
        if (normalizedVolume > _maxVolumeSeen)
        {
            _maxVolumeSeen = normalizedVolume;
        }

        // Log volume every second for debugging
        if ((DateTime.Now - _lastVolumeLog).TotalSeconds >= 1)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Volume: {normalizedVolume:F0} (max: {_maxVolumeSeen:F0}, threshold: {_config.VadThreshold})");
            _lastVolumeLog = DateTime.Now;
        }

        // Voice Activity Detection
        bool isSpeech = normalizedVolume > _config.VadThreshold;

        lock (_bufferLock)
        {
            var chunk = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, chunk, e.BytesRecorded);

            // Monitor-only mode: just keep the rolling pre-roll buffer warm.
            if (_isMonitoring)
            {
                AddToPreRoll(chunk);
                return;
            }

            // In long recording mode, always buffer audio regardless of VAD
            if (_isLongRecording)
            {
                _audioBuffer.Add(chunk);

                // Calculate chunk duration for tracking
                double chunkDuration = (double)e.BytesRecorded / (_config.SampleRate * 2);
                _accumulatedSpeechDuration += TimeSpan.FromSeconds(chunkDuration);
                _lastSpeechTime = DateTime.Now;
                return;
            }

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
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Speech STARTED. Pre-roll: {bufferDuration:F2}s");

                    // Add context from voice buffer
                    _audioBuffer.AddRange(_voiceBuffer);
                    _voiceBuffer.Clear();
                }

                // Calculate chunk duration
                double chunkDuration = (double)e.BytesRecorded / (_config.SampleRate * 2); // 16-bit = 2 bytes
                _accumulatedSpeechDuration += TimeSpan.FromSeconds(chunkDuration);

                _lastSpeechTime = DateTime.Now;
                _audioBuffer.Add(chunk);
            }
            else
            {
                // Silence - maintain pre-roll buffer
                AddToPreRoll(chunk);

                if (_isSpeechDetected)
                {
                    // Still recording after speech (trailing silence)
                    _audioBuffer.Add(chunk);

                    // In long recording mode, don't auto-finalize on silence
                    if (!_isLongRecording)
                    {
                        var silenceDuration = (DateTime.Now - _lastSpeechTime).TotalSeconds;
                        // Post-roll keeps recording a bit past the silence timeout.
                        var finalizeAfter = _config.MaxSilenceDuration + Math.Max(0, _config.PostRollSeconds);

                        if (silenceDuration > finalizeAfter)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Silence timeout ({silenceDuration:F2}s > {finalizeAfter:F2}s). Finalizing...");
                            // End of speech detected
                            FinalizeAudio();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Maximum bytes to retain in the rolling pre-roll buffer.
    /// When pre-roll is disabled, keeps a small floor (~250ms) so VAD word-starts
    /// aren't clipped.
    /// </summary>
    private int PreRollMaxBytes()
    {
        double seconds = _config.PreRollEnabled ? Math.Max(0, _config.PreRollSeconds) : 0.25;
        return Math.Max(1, (int)(seconds * _config.SampleRate * 2));
    }

    /// <summary>
    /// Appends a chunk to the rolling pre-roll buffer, trimming the oldest chunks
    /// to stay within the configured pre-roll duration. Caller must hold _bufferLock.
    /// </summary>
    private void AddToPreRoll(byte[] chunk)
    {
        _voiceBuffer.Add(chunk);
        int maxBytes = PreRollMaxBytes();
        long total = _voiceBuffer.Sum(b => b.Length);
        while (total > maxBytes && _voiceBuffer.Count > 1)
        {
            total -= _voiceBuffer[0].Length;
            _voiceBuffer.RemoveAt(0);
        }
    }

    private void FinalizeAudio()
    {
        // Calculate duration from first speech to last speech (excluding trailing silence)
        // This is the "speech span" - from when speech started to when it last ended
        var speechSpanDuration = _lastSpeechTime - _speechStartTime;

        // Use accumulated speech duration (actual time when audio was above threshold)
        var speechDuration = _accumulatedSpeechDuration;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Finalizing audio. Speech Span: {speechSpanDuration.TotalSeconds:F2}s, Active Speech: {speechDuration.TotalSeconds:F2}s (Min: {_config.MinAudioDuration}s)");

        // Always send audio - let the application layer decide based on transcription result
        // Combine all audio chunks
        long totalBytes = _audioBuffer.Sum(b => b.Length);
        var combinedAudio = new byte[totalBytes];
        int offset = 0;

        foreach (var chunk in _audioBuffer)
        {
            Array.Copy(chunk, 0, combinedAudio, offset, chunk.Length);
            offset += chunk.Length;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Audio ready. Total size: {totalBytes} bytes");
        AudioReady?.Invoke(this, new AudioReadyEventArgs(combinedAudio, speechSpanDuration, speechDuration));

        // Reset state
        _audioBuffer.Clear();
        _accumulatedSpeechDuration = TimeSpan.Zero;
        _isSpeechDetected = false;
        SpeechDetected?.Invoke(this, false);
    }

    private static double CalculateRms(byte[] buffer, int bytesRecorded)
    {
        double sum = 0;
        int sampleCount = bytesRecorded / 2; // 16-bit samples

        for (int i = 0; i < bytesRecorded; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            sum += sample * sample;
        }

        return Math.Sqrt(sum / sampleCount) / 32768.0;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Console.WriteLine($"Recording stopped due to error: {e.Exception.Message}");
        }
    }

    public void StartLongRecording()
    {
        if (_isLongRecording || !_isRecording) return;

        lock (_bufferLock)
        {
            _isLongRecording = true;
            _isMonitoring = false;
            _longRecordingStartTime = DateTime.Now;
            _isSpeechDetected = true; // Treat as always "speaking"
            _speechStartTime = DateTime.Now;
            _accumulatedSpeechDuration = TimeSpan.Zero;

            _audioBuffer.Clear();

            // Prepend the rolling pre-roll buffer so audio from just before the
            // keypress is included.
            if (_config.PreRollEnabled && _voiceBuffer.Count > 0)
            {
                _audioBuffer.AddRange(_voiceBuffer);
                long preBytes = _voiceBuffer.Sum(b => b.Length);
                var preRoll = TimeSpan.FromSeconds((double)preBytes / (_config.SampleRate * 2));
                _accumulatedSpeechDuration = preRoll;
                _speechStartTime = DateTime.Now - preRoll;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Long recording STARTED with {preRoll.TotalSeconds:F2}s pre-roll");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Long recording STARTED");
            }
            _voiceBuffer.Clear();

            SpeechDetected?.Invoke(this, true);
        }
    }

    public void StopLongRecording()
    {
        if (!_isLongRecording) return;

        lock (_bufferLock)
        {
            _isLongRecording = false;
            var duration = DateTime.Now - _longRecordingStartTime;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Long recording STOPPED. Duration: {duration.TotalSeconds:F2}s");

            // Finalize and send all buffered audio
            if (_audioBuffer.Count > 0)
            {
                FinalizeAudio();
            }
            else
            {
                // No audio captured, reset state
                _isSpeechDetected = false;
                SpeechDetected?.Invoke(this, false);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopDevice();
        _waveIn?.Dispose();
        _disposed = true;
    }
}
