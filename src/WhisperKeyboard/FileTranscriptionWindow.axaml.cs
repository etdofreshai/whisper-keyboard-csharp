using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WhisperKeyboard.Core;

namespace WhisperKeyboard;

public partial class FileTranscriptionWindow : Window
{
    private readonly SpeechTranscriber _transcriber;
    private CancellationTokenSource? _cts;
    private bool _isBusy;

    private static readonly HashSet<string> WhisperCompatible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".webm"
    };

    private static readonly HashSet<string> AllSupported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".webm",
        ".mp4", ".mov", ".mkv", ".avi", ".wma", ".aac"
    };

    public FileTranscriptionWindow() : this(new SpeechTranscriber(Config.Load())) { }

    public FileTranscriptionWindow(SpeechTranscriber transcriber)
    {
        InitializeComponent();
        _transcriber = transcriber;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var item in files)
        {
            if (item is IStorageFile file)
            {
                var path = file.Path.LocalPath;
                if (!string.IsNullOrEmpty(path))
                {
                    await ProcessFileAsync(path);
                    return;
                }
            }
        }
    }

    private async void OnDropZoneClicked(object? sender, PointerPressedEventArgs e)
    {
        if (_isBusy) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select audio or video file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio/Video Files")
                {
                    Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.m4a", "*.flac", "*.webm", "*.mp4", "*.mov", "*.mkv", "*.avi", "*.wma", "*.aac" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
                await ProcessFileAsync(path);
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        if (_isBusy) return;
        _isBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!AllSupported.Contains(ext))
            {
                SetStatus($"Unsupported format: {ext}");
                return;
            }

            FileNameText.Text = Path.GetFileName(filePath);
            ResultTextBox.Text = "";
            CopyButton.IsEnabled = false;
            ProgressBar.IsVisible = true;

            string fileToTranscribe = filePath;

            // Convert if not Whisper-compatible
            if (!WhisperCompatible.Contains(ext))
            {
                SetStatus("Converting with ffmpeg...");
                var tempPath = Path.Combine(Path.GetTempPath(), $"whisper_{Guid.NewGuid()}.ogg");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{filePath}\" -vn -c:a libvorbis -y \"{tempPath}\"",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) throw new Exception("Failed to start ffmpeg. Is it installed and on PATH?");

                    await proc.WaitForExitAsync(_cts.Token);

                    if (proc.ExitCode != 0)
                    {
                        var stderr = await proc.StandardError.ReadToEndAsync();
                        throw new Exception($"ffmpeg failed (exit {proc.ExitCode}): {stderr.Substring(0, Math.Min(200, stderr.Length))}");
                    }

                    fileToTranscribe = tempPath;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    throw new Exception("ffmpeg not found. Please install ffmpeg and ensure it's on your PATH.");
                }
            }

            // Check file size (Whisper API limit is 25MB)
            var fileSize = new FileInfo(fileToTranscribe).Length;
            if (fileSize > 25 * 1024 * 1024)
            {
                SetStatus($"File too large ({fileSize / 1024.0 / 1024.0:F1} MB). Max is 25 MB.");
                return;
            }

            SetStatus("Transcribing...");
            var result = await _transcriber.TranscribeFileAsync(fileToTranscribe, _cts.Token);

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                ResultTextBox.Text = result.Text;
                CopyButton.IsEnabled = true;
                SetStatus("Done!");
            }
            else
            {
                SetStatus("No speech detected in file.");
            }

            // Clean up temp file
            if (fileToTranscribe != filePath && File.Exists(fileToTranscribe))
            {
                try { File.Delete(fileToTranscribe); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Console.WriteLine($"File transcription error: {ex}");
        }
        finally
        {
            ProgressBar.IsVisible = false;
            _isBusy = false;
        }
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && !string.IsNullOrEmpty(ResultTextBox.Text))
        {
            await clipboard.SetTextAsync(ResultTextBox.Text);
            var btn = (Button)sender!;
            btn.Content = "Copied!";
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.UIThread.Post(() => btn.Content = "Copy"));
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnClosing(e);
    }
}
