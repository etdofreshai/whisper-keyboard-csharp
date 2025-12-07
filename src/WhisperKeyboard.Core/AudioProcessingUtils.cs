namespace WhisperKeyboard.Core;

/// <summary>
/// Portable audio processing utilities (VAD, RMS calculation, etc.)
/// </summary>
public static class AudioProcessingUtils
{
    /// <summary>
    /// Calculate RMS (Root Mean Square) volume from 16-bit PCM audio buffer.
    /// </summary>
    /// <param name="buffer">Audio buffer containing 16-bit PCM samples</param>
    /// <param name="bytesRecorded">Number of bytes to process</param>
    /// <returns>Normalized RMS value (0.0 to 1.0)</returns>
    public static double CalculateRms(byte[] buffer, int bytesRecorded)
    {
        double sum = 0;
        int sampleCount = bytesRecorded / 2; // 16-bit samples

        for (int i = 0; i < bytesRecorded; i += 2)
        {
            if (i + 1 < buffer.Length)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                sum += sample * sample;
            }
        }

        if (sampleCount == 0) return 0;
        return Math.Sqrt(sum / sampleCount) / 32768.0;
    }

    /// <summary>
    /// Calculate normalized volume (0-32768 range) from RMS.
    /// </summary>
    public static double GetNormalizedVolume(byte[] buffer, int bytesRecorded)
    {
        return CalculateRms(buffer, bytesRecorded) * 32768;
    }

    /// <summary>
    /// Determine if audio represents speech based on volume threshold.
    /// </summary>
    public static bool IsSpeech(byte[] buffer, int bytesRecorded, int vadThreshold)
    {
        return GetNormalizedVolume(buffer, bytesRecorded) > vadThreshold;
    }
}
