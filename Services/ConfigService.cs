using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using KerkenezSpeech.Models;

namespace KerkenezSpeech.Services;

public class ConfigService
{
    public const string CurrentVersion = "0.1.0";
    private readonly string _configFilePath;
    private readonly string _configFolderPath;
    private AppConfig _config;

    public AppConfig Config => _config;
    public string ConfigFolderPath => _configFolderPath;
    public string ConfigFilePath => _configFilePath;

    public string ActiveModelId => !string.IsNullOrWhiteSpace(_config.ModelId)
        ? _config.ModelId
        : DetectModelId(_config.ModelPath);

    public event Action<AppConfig>? ConfigChanged;

    public ConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configFolderPath = Path.Combine(appData, "Kerkenez", "speech");
        Directory.CreateDirectory(_configFolderPath);
        _configFilePath = Path.Combine(_configFolderPath, "config.json");

        _config = Load();
        ValidateOrFindModelPath();

        // Ensure ModelId and LanguageCode are consistent
        if (string.IsNullOrWhiteSpace(_config.ModelId))
        {
            _config.ModelId = DetectModelId(_config.ModelPath);
        }
        _config.LanguageCode = SupportedLanguage.NormalizeLanguageForModel(ActiveModelId, _config.LanguageCode);

        if (!File.Exists(_configFilePath))
        {
            Save();
        }
    }

    private AppConfig Load()
    {
        try
        {
            // Also check if config.json exists beside the executable first
            string localConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            string pathToRead = File.Exists(localConfig) ? localConfig : _configFilePath;

            if (File.Exists(pathToRead))
            {
                string json = File.ReadAllText(pathToRead);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    loaded.Version = CurrentVersion;
                    return loaded;
                }
            }
        }
        catch { }

        var fresh = new AppConfig { Version = CurrentVersion };
        return fresh;
    }

    public void Save()
    {
        try
        {
            _config.Version = CurrentVersion;
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configFilePath, json);
            ConfigChanged?.Invoke(_config);
        }
        catch { }
    }

    public void UpdateLanguage(string languageCode)
    {
        _config.LanguageCode = SupportedLanguage.NormalizeLanguageForModel(ActiveModelId, languageCode);
        Save();
    }

    public void UpdateModel(string modelId, string modelPath, string modelName)
    {
        _config.ModelId = modelId;
        _config.ModelPath = modelPath;
        _config.ModelName = modelName;
        _config.LanguageCode = SupportedLanguage.NormalizeLanguageForModel(modelId, _config.LanguageCode);
        Save();
    }

    public void UpdateInputDevice(int deviceIndex)
    {
        _config.InputDeviceIndex = deviceIndex;
        Save();
    }

    public void UpdateOpenMicMode(bool openMic)
    {
        _config.OpenMicMode = openMic;
        Save();
    }

    public void UpdateTypingMode(TypingMode mode)
    {
        _config.TypingMode = mode;
        Save();
    }

    public void UpdateRamCacheSeconds(int seconds)
    {
        _config.RamCacheSeconds = seconds;
        Save();
    }

    public void UpdateAddTrailingSpace(bool addSpace)
    {
        _config.AddTrailingSpace = addSpace;
        Save();
    }

    public void UpdateAutoStart(bool autoStart)
    {
        _config.AutoStartOnBoot = autoStart;
        SetAutoStart(autoStart);
        Save();
    }

    /// <summary>
    /// Configures automatic startup in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// Also removes any obsolete startup folder shortcut if present.
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
        try
        {
            // 1. Remove any legacy or duplicate shortcut in the Windows Startup folder
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolder, "KerkenezSpeech.lnk");
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }

            // 2. Set or remove in HKCU Run registry
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (runKey != null)
            {
                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KerkenezSpeech.exe");
                    }
                    runKey.SetValue("KerkenezSpeech", $"\"{exePath}\"");
                }
                else
                {
                    runKey.DeleteValue("KerkenezSpeech", false);
                    runKey.DeleteValue("SpeechRecognation", false);
                }
            }
        }
        catch { }
    }

    public IReadOnlyList<SupportedLanguage> GetSupportedLanguagesForActiveModel()
    {
        return SupportedLanguage.GetSupportedLanguages(ActiveModelId);
    }

    public static string DetectModelId(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            return "nemotron-int8";

        string lower = modelPath.ToLowerInvariant();
        if (lower.Contains("zipformer-en") || lower.Contains("chunk-16"))
            return "zipformer-en";
        if (lower.Contains("bilingual") || lower.Contains("zh-en"))
            return "zipformer-bilingual";
        if (lower.Contains("nemotron"))
            return "nemotron-int8";

        if (Directory.Exists(modelPath))
        {
            var files = Directory.GetFiles(modelPath).Select(Path.GetFileName).ToList();
            if (files.Any(f => f != null && f.Contains("chunk-16", StringComparison.OrdinalIgnoreCase)))
                return "zipformer-en";
            if (files.Any(f => f != null && (f.Contains("bilingual", StringComparison.OrdinalIgnoreCase) || f.Contains("avg-1.int8.onnx", StringComparison.OrdinalIgnoreCase))))
                return "zipformer-bilingual";
        }

        return "nemotron-int8";
    }

    public static string GetModelType(string? modelId)
    {
        return modelId?.ToLowerInvariant() switch
        {
            "zipformer-en" => "zipformer",
            "zipformer-bilingual" => "zipformer",
            _ => "nemotron"
        };
    }

    public void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _configFolderPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ValidateOrFindModelPath()
    {
        if (!string.IsNullOrWhiteSpace(_config.ModelPath) && Directory.Exists(_config.ModelPath) && IsValidModelDir(_config.ModelPath))
        {
            return;
        }

        var (discovered, discoveredId, discoveredName) = SetupEngine.FindBestLocalModel();
        if (!string.IsNullOrEmpty(discovered))
        {
            _config.ModelPath = discovered;
            _config.ModelId = discoveredId ?? DetectModelId(discovered);
            _config.ModelName = discoveredName ?? _config.ModelName;
            _config.LanguageCode = SupportedLanguage.NormalizeLanguageForModel(_config.ModelId, _config.LanguageCode);
            Save();
        }
    }

    public static (string? Encoder, string? Decoder, string? Joiner, string? Tokens) ResolveTransducerFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return (null, null, null, null);

        var onnxFiles = Directory.GetFiles(path, "*.onnx");

        // Prefer .int8.onnx if available, otherwise any matching .onnx
        string? encoder = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("encoder", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".int8.onnx", StringComparison.OrdinalIgnoreCase))
                       ?? onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("encoder", StringComparison.OrdinalIgnoreCase));

        string? decoder = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("decoder", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".int8.onnx", StringComparison.OrdinalIgnoreCase))
                       ?? onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("decoder", StringComparison.OrdinalIgnoreCase));

        string? joiner = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("joiner", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".int8.onnx", StringComparison.OrdinalIgnoreCase))
                      ?? onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("joiner", StringComparison.OrdinalIgnoreCase));

        string defaultTokens = Path.Combine(path, "tokens.txt");
        string? tokens = File.Exists(defaultTokens) ? defaultTokens : Directory.GetFiles(path, "*tokens*.txt").FirstOrDefault();

        return (encoder, decoder, joiner, tokens);
    }

    public static bool IsValidModelDir(string path)
    {
        var (enc, dec, joi, tok) = ResolveTransducerFiles(path);
        return !string.IsNullOrEmpty(enc) && !string.IsNullOrEmpty(dec) && !string.IsNullOrEmpty(joi) && !string.IsNullOrEmpty(tok);
    }
}
