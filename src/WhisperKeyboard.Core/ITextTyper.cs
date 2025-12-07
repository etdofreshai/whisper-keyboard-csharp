namespace WhisperKeyboard.Core;

/// <summary>
/// Platform-agnostic interface for typing/pasting text.
/// Implementations will be platform-specific.
/// </summary>
public interface ITextTyper
{
    /// <summary>
    /// Type or paste the given text.
    /// </summary>
    Task TypeTextAsync(string text);

    /// <summary>
    /// Send backspace key(s).
    /// </summary>
    void SendBackspace(int count = 1);

    /// <summary>
    /// Send enter/return key.
    /// </summary>
    void SendEnter();
}
