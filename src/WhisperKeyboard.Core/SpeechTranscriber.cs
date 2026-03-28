using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Lame;
using NAudio.Wave;

namespace WhisperKeyboard.Core;

public class SpeechTranscriber : IDisposable
{
    private readonly Config _config;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public SpeechTranscriber(Config config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<TranscriptionResult?> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key not configured");
            return null;
        }

        try
        {
            // Convert raw PCM to WAV format
            byte[] wavData = ConvertToWav(audioData, _config.SampleRate, _config.Channels);

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            using var content = new MultipartFormDataContent();

            // Add the audio file
            var audioContent = new ByteArrayContent(wavData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");

            // Add the model parameter
            content.Add(new StringContent(_config.Model), "model");

            // Add language if specified (not "auto")
            if (!string.IsNullOrEmpty(_config.Language) && _config.Language != "auto")
            {
                content.Add(new StringContent(_config.Language), "language");
            }

            // Add response format
            content.Add(new StringContent("verbose_json"), "response_format");

            request.Content = content;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode} - {responseBody}");
                return null;
            }

            var result = JsonSerializer.Deserialize<WhisperApiResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return null;
            }

            return BuildResult(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Transcription error: {ex.Message}");
            return null;
        }
    }

    private static byte[] ConvertToWav(byte[] pcmData, int sampleRate, int channels)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);

        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);

        // RIFF header
        writer.Write(new char[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + pcmData.Length); // File size - 8
        writer.Write(new char[] { 'W', 'A', 'V', 'E' });

        // fmt chunk
        writer.Write(new char[] { 'f', 'm', 't', ' ' });
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(new char[] { 'd', 'a', 't', 'a' });
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return memoryStream.ToArray();
    }

    private static byte[] ConvertToMp3(byte[] pcmData, int sampleRate, int channels)
    {
        using var outputStream = new MemoryStream();
        var waveFormat = new WaveFormat(sampleRate, 16, channels);

        using (var writer = new LameMP3FileWriter(outputStream, waveFormat, 64)) // 64kbps
        {
            writer.Write(pcmData, 0, pcmData.Length);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    /// Try to convert PCM to MP3. Returns null if LAME is not available (e.g., on macOS).
    /// </summary>
    private static byte[]? TryConvertToMp3(byte[] pcmData, int sampleRate, int channels)
    {
        try
        {
            return ConvertToMp3(pcmData, sampleRate, channels);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MP3 encoding not available: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Transcribe long recordings using MP3 encoding to reduce file size.
    /// Falls back to WAV if MP3 encoding is not available (e.g., on macOS without LAME).
    /// </summary>
    public async Task<TranscriptionResult?> TranscribeLongRecordingAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key not configured");
            return null;
        }

        try
        {
            // Try to convert to MP3 (smaller), fall back to WAV if LAME is not available
            byte[] encodedData;
            string contentType;
            string fileName;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Converting {audioData.Length / 1024.0 / 1024.0:F2} MB PCM...");

            var mp3Data = TryConvertToMp3(audioData, _config.SampleRate, _config.Channels);
            if (mp3Data != null)
            {
                encodedData = mp3Data;
                contentType = "audio/mpeg";
                fileName = "audio.mp3";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MP3 size: {encodedData.Length / 1024.0 / 1024.0:F2} MB");
            }
            else
            {
                // Fallback to WAV format
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Falling back to WAV format...");
                encodedData = ConvertToWav(audioData, _config.SampleRate, _config.Channels);
                contentType = "audio/wav";
                fileName = "audio.wav";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] WAV size: {encodedData.Length / 1024.0 / 1024.0:F2} MB");
            }

            // Use a longer timeout for long recordings (5 minutes)
            using var longTimeoutClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            using var content = new MultipartFormDataContent();

            // Add the audio file
            var audioContent = new ByteArrayContent(encodedData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(audioContent, "file", fileName);

            // Add the model parameter
            content.Add(new StringContent(_config.Model), "model");

            // Add language if specified (not "auto")
            if (!string.IsNullOrEmpty(_config.Language) && _config.Language != "auto")
            {
                content.Add(new StringContent(_config.Language), "language");
            }

            // Add response format
            content.Add(new StringContent("verbose_json"), "response_format");

            request.Content = content;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending long recording to Whisper API...");
            var response = await longTimeoutClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode} - {responseBody}");
                return null;
            }

            var result = JsonSerializer.Deserialize<WhisperApiResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return null;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Long recording transcription complete");
            return BuildResult(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Long recording transcription error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Transcribe an audio/video file from disk. The file should already be in a Whisper-compatible format.
    /// </summary>
    public async Task<TranscriptionResult?> TranscribeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            Console.WriteLine("Error: OpenAI API key not configured");
            return null;
        }

        try
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".m4a" => "audio/mp4",
                ".flac" => "audio/flac",
                ".webm" => "audio/webm",
                _ => "application/octet-stream"
            };

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Transcribing file: {filePath} ({fileBytes.Length / 1024.0 / 1024.0:F2} MB)");

            using var longTimeoutClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(fileBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(audioContent, "file", Path.GetFileName(filePath));
            content.Add(new StringContent(_config.Model), "model");

            if (!string.IsNullOrEmpty(_config.Language) && _config.Language != "auto")
                content.Add(new StringContent(_config.Language), "language");

            content.Add(new StringContent("verbose_json"), "response_format");
            request.Content = content;

            var response = await longTimeoutClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode} - {responseBody}");
                throw new Exception($"API Error: {response.StatusCode} - {responseBody}");
            }

            var result = JsonSerializer.Deserialize<WhisperApiResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result == null ? null : BuildResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex.Message.StartsWith("API Error:")) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"File transcription error: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _httpClient.Dispose();
        _disposed = true;
    }

    private static TranscriptionResult BuildResult(WhisperApiResponse response)
    {
        var result = new TranscriptionResult
        {
            Text = response.Text ?? "",
            Language = response.Language ?? ""
        };

        if (response.Segments != null && response.Segments.Count > 0)
        {
            result.AvgLogProb = response.Segments.Average(s => s.AvgLogprob);
            result.NoSpeechProb = response.Segments.Max(s => s.NoSpeechProb);
            result.CompressionRatio = response.Segments.Average(s => s.CompressionRatio);

            // Convert avg_logprob to a 0-1 confidence score
            // Typical range: -1.0 (low confidence) to 0.0 (high confidence)
            result.Confidence = Math.Clamp(1.0 + result.AvgLogProb, 0.0, 1.0);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Whisper confidence: avg_logprob={result.AvgLogProb:F3}, no_speech_prob={result.NoSpeechProb:F3}, compression_ratio={result.CompressionRatio:F2}, words={result.WordCount}");
        }
        else
        {
            result.Confidence = 1.0;
        }

        return result;
    }

    private class WhisperApiResponse
    {
        public string? Text { get; set; }
        public string? Language { get; set; }
        public double? Duration { get; set; }
        public List<WhisperSegment>? Segments { get; set; }
    }

    private class WhisperSegment
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public double Start { get; set; }
        public double End { get; set; }
        [JsonPropertyName("avg_logprob")]
        public double AvgLogprob { get; set; }
        [JsonPropertyName("no_speech_prob")]
        public double NoSpeechProb { get; set; }
        [JsonPropertyName("compression_ratio")]
        public double CompressionRatio { get; set; } = 1.0;
    }
}
