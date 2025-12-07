namespace WhisperKeyboard.Core;

/// <summary>
/// Platform-agnostic interface for audio capture.
/// Implementations will be platform-specific (OpenAL, NAudio, etc.)
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// Fired when audio data is ready for transcription.
    /// </summary>
    event EventHandler<byte[]>? AudioReady;

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
}
