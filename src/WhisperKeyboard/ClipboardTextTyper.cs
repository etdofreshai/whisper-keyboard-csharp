using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input.Platform;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

/// <summary>
/// Cross-platform text typer using clipboard paste.
/// Uses Avalonia's built-in clipboard support and platform-specific key simulation.
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

        // Log clipboard state at initialization
        if (_clipboard == null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] WARNING: Clipboard is NULL - paste mode will fall back to direct typing");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Clipboard initialized successfully");
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] PasteMode={config.PasteMode}");
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SKIPPING duplicate text: \"{text}\" (last typed {(DateTime.Now - _lastTypeTime).TotalSeconds:F1}s ago)");
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            _lastTypedText = text;
            _lastTypeTime = DateTime.Now;
        }

        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (_config.PasteMode && _clipboard != null)
                {
                    // Paste mode: copy to clipboard and simulate Cmd+V
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using PASTE mode (clipboard available)");
                    await _clipboard.SetTextAsync(text);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Text copied to clipboard: \"{text}\"");

                    // Delay to ensure clipboard/pasteboard is fully updated
                    await Task.Delay(200);

                    // Simulate Cmd+V (paste) on macOS
                    await SimulatePasteAsync();
                }
                else
                {
                    // Typing mode: simulate individual keystrokes
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Using TYPING mode (PasteMode={_config.PasteMode}, clipboard={(_clipboard != null ? "available" : "NULL")})");
                    await SimulateTypingAsync(text);
                }
            }

            if (_config.AutoEnter || shouldEnter)
            {
                await Task.Delay(50);
                await SimulateEnterAsync();
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] TypeTextAsync completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in TypeTextAsync: {ex.Message}");
            throw;
        }
    }

    private static async Task SimulatePasteAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsInputSender.SendPaste();
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSInputSender.SendPaste();
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Use xdotool on Linux
            await RunProcessAsync("xdotool", "key ctrl+v");
        }
    }

    private static async Task SimulateEnterAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsInputSender.SendEnter();
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSInputSender.SendEnter();
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await RunProcessAsync("xdotool", "key Return");
        }
    }

    private static async Task SimulateTypingAsync(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsInputSender.SendText(text);
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSInputSender.SendText(text);
            await Task.CompletedTask;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // xdotool can type text directly
            await RunProcessAsync("xdotool", $"type -- \"{text}\"");
        }
    }

    private static async Task RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Process error: {ex.Message}");
        }
    }

    public void SendBackspace(int count = 1)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            for (int i = 0; i < count; i++)
                MacOSInputSender.SendBackspace();
        }
    }

    public void SendEnter()
    {
        _ = SimulateEnterAsync();
    }
}
