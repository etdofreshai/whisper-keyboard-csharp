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

    public event EventHandler<AudioReadyEventArgs>? AudioReady;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<bool>? SpeechDetected;

    public bool IsRecording => _isRecording;
    public bool IsPaused { get; private set; }
    public bool IsSpeechDetected => _isSpeechDetected;

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

    public void Start()
    {
        if (_isRecording) return;

        _waveIn = new WaveInEvent
        {
            DeviceNumber = _config.DeviceIndex >= 0 ? _config.DeviceIndex : 0,
            WaveFormat = new WaveFormat(_config.SampleRate, 16, _config.Channels),
            BufferMilliseconds = 50
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            _waveIn.StartRecording();
            _isRecording = true;
            IsPaused = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting audio recording: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRecording) return;

        _isRecording = false;
        _waveIn?.StopRecording();
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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Silence timeout ({silenceDuration:F2}s > {_config.MaxSilenceDuration}s). Finalizing...");
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

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _waveIn?.Dispose();
        _disposed = true;
    }
}
