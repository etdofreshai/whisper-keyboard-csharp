namespace WhisperKeyboard.Core;

/// <summary>
/// Event args for AudioReady event, containing audio data and metadata.
/// </summary>
public class AudioReadyEventArgs : EventArgs
{
    /// <summary>
    /// The raw audio data (PCM 16-bit).
    /// </summary>
    public byte[] AudioData { get; }

    /// <summary>
    /// Duration from first speech to last speech (excluding trailing silence).
    /// This is the "speech span" used for minimum duration checks.
    /// </summary>
    public TimeSpan TotalDuration { get; }

    /// <summary>
    /// Duration of actual speech (time above VAD threshold).
    /// </summary>
    public TimeSpan SpeechDuration { get; }

    public AudioReadyEventArgs(byte[] audioData, TimeSpan totalDuration, TimeSpan speechDuration)
    {
        AudioData = audioData;
        TotalDuration = totalDuration;
        SpeechDuration = speechDuration;
    }
}

/// <summary>
/// Platform-agnostic interface for audio capture.
/// Implementations will be platform-specific (OpenAL, NAudio, etc.)
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// Fired when audio data is ready for transcription.
    /// </summary>
    event EventHandler<AudioReadyEventArgs>? AudioReady;

    /// <summary>
    /// Fired when volume level changes.
    /// </summary>
    event EventHandler<double>? VolumeChanged;

    /// <summary>
    /// Fired when speech detection state changes.
    /// </summary>
    event EventHandler<bool>? SpeechDetected;

    /// <summary>
    /// Whether audio capture is currently active.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Whether capture is paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Whether speech is currently being detected.
    /// </summary>
    bool IsSpeechDetected { get; }

    /// <summary>
    /// Get list of available audio input devices.
    /// </summary>
    List<string> GetAudioDevices();

    /// <summary>
    /// Start audio capture.
    /// </summary>
    void Start();

    /// <summary>
    /// Stop audio capture.
    /// </summary>
    void Stop();

    /// <summary>
    /// Pause audio capture without stopping.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resume paused audio capture.
    /// </summary>
    void Resume();

    /// <summary>
    /// Start long recording mode (bypasses VAD auto-stop).
    /// Audio will continue buffering until StopLongRecording is called.
    /// </summary>
    void StartLongRecording();

    /// <summary>
    /// Stop long recording and return all buffered audio via AudioReady event.
    /// </summary>
    void StopLongRecording();

    /// <summary>
    /// Whether long recording mode is active.
    /// </summary>
    bool IsLongRecording { get; }
}
