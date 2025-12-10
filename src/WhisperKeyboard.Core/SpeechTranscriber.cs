using System.Net.Http.Headers;
using System.Text.Json;
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

            return new TranscriptionResult
            {
                Text = result.Text ?? "",
                Language = result.Language ?? "",
                Confidence = 1.0
            };
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
    /// Transcribe long recordings using MP3 encoding to reduce file size.
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
            // Convert raw PCM to MP3 format (much smaller than WAV)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Converting {audioData.Length / 1024.0 / 1024.0:F2} MB PCM to MP3...");
            byte[] mp3Data = ConvertToMp3(audioData, _config.SampleRate, _config.Channels);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] MP3 size: {mp3Data.Length / 1024.0 / 1024.0:F2} MB");

            // Use a longer timeout for long recordings (5 minutes)
            using var longTimeoutClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            using var content = new MultipartFormDataContent();

            // Add the audio file
            var audioContent = new ByteArrayContent(mp3Data);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            content.Add(audioContent, "file", "audio.mp3");

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
            return new TranscriptionResult
            {
                Text = result.Text ?? "",
                Language = result.Language ?? "",
                Confidence = 1.0
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Long recording transcription error: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _httpClient.Dispose();
        _disposed = true;
    }

    private class WhisperApiResponse
    {
        public string? Text { get; set; }
        public string? Language { get; set; }
        public double? Duration { get; set; }
    }
}
