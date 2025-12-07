namespace WhisperKeyboard.Core;

public class HistoryEntry
{
    public string FullText { get; set; } = "";
    public DateTime Timestamp { get; set; }

    public string PreviewText => FullText.Length > 50
        ? FullText[..50] + "..."
        : FullText;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}
