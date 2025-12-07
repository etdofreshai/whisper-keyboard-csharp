using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input.Platform;
using WhisperKeyboard.Core;

namespace WhisperKeyboard.Avalonia;

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
            if (!string.IsNullOrEmpty(text))
            {
                if (_config.PasteMode && _clipboard != null)
                {
                    // Paste mode: copy to clipboard and simulate Cmd+V
                    await _clipboard.SetTextAsync(text);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Text copied to clipboard: \"{text}\"");

                    // Small delay to ensure clipboard is set
                    await Task.Delay(50);

                    // Simulate Cmd+V (paste) on macOS
                    await SimulatePasteAsync();
                }
                else
                {
                    // Typing mode: simulate individual keystrokes
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Typing text directly: \"{text}\"");
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Use AppleScript to simulate Cmd+V on macOS
            await RunAppleScriptAsync("tell application \"System Events\" to keystroke \"v\" using command down");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Use xdotool on Linux
            await RunProcessAsync("xdotool", "key ctrl+v");
        }
        // Windows would use SendInput but that's handled by the Windows-specific TextTyper
    }

    private static async Task SimulateEnterAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await RunAppleScriptAsync("tell application \"System Events\" to keystroke return");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await RunProcessAsync("xdotool", "key Return");
        }
    }

    private static async Task SimulateTypingAsync(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Escape special characters for AppleScript
            var escapedText = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            await RunAppleScriptAsync($"tell application \"System Events\" to keystroke \"{escapedText}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // xdotool can type text directly
            await RunProcessAsync("xdotool", $"type -- \"{text}\"");
        }
    }

    private static async Task RunAppleScriptAsync(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                // Write script to stdin instead of passing via arguments
                await process.StandardInput.WriteLineAsync(script);
                process.StandardInput.Close();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] AppleScript error: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] AppleScript error: {ex.Message}");
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
            {
                _ = RunAppleScriptAsync("tell application \"System Events\" to key code 51");
            }
        }
    }

    public void SendEnter()
    {
        _ = SimulateEnterAsync();
    }
}
