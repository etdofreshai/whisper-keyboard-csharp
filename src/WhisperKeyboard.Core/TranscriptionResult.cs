namespace WhisperKeyboard.Core;

public class TranscriptionResult
{
    public string Text { get; set; } = "";
    public string Language { get; set; } = "";
    public double Confidence { get; set; } = 1.0;
}
