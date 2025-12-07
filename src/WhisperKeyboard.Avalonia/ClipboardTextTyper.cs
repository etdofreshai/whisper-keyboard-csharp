using Avalonia;
using Avalonia.Input.Platform;
using WhisperKeyboard.Core;

namespace WhisperKeyboard.Avalonia;

/// <summary>
/// Cross-platform text typer using clipboard paste.
/// Uses Avalonia's built-in clipboard support.
/// </summary>
public class ClipboardTextTyper : ITextTyper
{
    private readonly Config _config;
    private readonly TextProcessor _textProcessor;
    private readonly IClipboard? _clipboard;
    private string _lastTypedText = "";
    private DateTime _lastTypeTime = DateTime.MinValue;

    public ClipboardTextTyper(Config config, IClipboard? clipboard)
    {
        _config = config;
        _textProcessor = new TextProcessor(config);
        _clipboard = clipboard;
    }

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync called with: \"{text}\"");

        // Process and clean text
        var (processedText, shouldEnter) = _textProcessor.ProcessText(text);
        text = processedText;

        // Check for duplicate text (within 2 seconds)
        if (!string.IsNullOrEmpty(text) && text == _lastTypedText && (DateTime.Now - _lastTypeTime).TotalSeconds < 2)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Skipping duplicate text");
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            _lastTypedText = text;
            _lastTypeTime = DateTime.Now;
        }

        try
        {
            if (!string.IsNullOrEmpty(text) && _clipboard != null)
            {
                // Set text to clipboard
                await _clipboard.SetTextAsync(text);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Text copied to clipboard: \"{text}\"");

                // Note: Actual Ctrl+V paste requires platform-specific implementation
                // For now, we just copy to clipboard and user can paste manually
                // or we can integrate with platform-specific key simulation later
            }

            if (_config.AutoEnter || shouldEnter)
            {
                // TODO: Send Enter key (requires platform-specific implementation)
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Would send Enter key");
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in TypeTextAsync: {ex.Message}");
            throw;
        }
    }

    public void SendBackspace(int count = 1)
    {
        // TODO: Platform-specific implementation
        Console.WriteLine($"SendBackspace({count}) - not implemented for cross-platform");
    }

    public void SendEnter()
    {
        // TODO: Platform-specific implementation
        Console.WriteLine("SendEnter() - not implemented for cross-platform");
    }
}
