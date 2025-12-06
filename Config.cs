using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperKeyboard;

public class Config
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhisperKeyboard");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");

    // Audio Settings
    public int SampleRate { get; set; } = 16000;
    public int ChunkSize { get; set; } = 1024;
    public int Channels { get; set; } = 1;
    public int DeviceIndex { get; set; } = -1; // -1 = default device
    public int VadThreshold { get; set; } = 500; // Lower default - adjust based on your mic
    public double SilenceThreshold { get; set; } = 0.5;
    public double MinSpeechDuration { get; set; } = 0.5;
    public double MaxSilenceDuration { get; set; } = 1.0;
    public double MinAudioDuration { get; set; } = 1.5;

    // OpenAI Settings
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "whisper-1";
    public string Language { get; set; } = "en";

    // Typing Settings
    public double TypingSpeed { get; set; } = 0.001;
    public bool AddPunctuation { get; set; } = true;
    public bool CapitalizeSentences { get; set; } = true;
    public bool PasteMode { get; set; } = false;
    public bool AutoEnter { get; set; } = false;
    public bool ExitWordsEnabled { get; set; } = true;
    public List<string> ExitWords { get; set; } = new List<string> { "over", "enter", "submit" };

    // Hotkey Settings
    public string ToggleRecordingHotkey { get; set; } = "Ctrl+Shift+R";
    public string PauseResumeHotkey { get; set; } = "Ctrl+Shift+P";
    public string QuitAppHotkey { get; set; } = "Ctrl+Shift+Q";

    // General Settings
    public bool ShowNotifications { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool IsCalibrated { get; set; } = false;

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<Config>(json);
                if (config != null)
                {
                    // Override API key from environment if present
                    var envApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    if (!string.IsNullOrEmpty(envApiKey))
                    {
                        config.ApiKey = envApiKey;
                    }
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
        }

        var defaultConfig = new Config();

        // Try to get API key from environment
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            defaultConfig.ApiKey = apiKey;
        }

        defaultConfig.Save();
        return defaultConfig;
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }

    public static string GetConfigPath() => ConfigFilePath;
}
