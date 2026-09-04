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

    public event Action<AppConfig>? ConfigChanged;

    public ConfigService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configFolderPath = Path.Combine(appData, "Kerkenez", "speech");
        Directory.CreateDirectory(_configFolderPath);
        _configFilePath = Path.Combine(_configFolderPath, "config.json");

        _config = Load();
        ValidateOrFindModelPath();
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
                    // Ensure current version is marked
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
        _config.LanguageCode = languageCode;
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
    /// Configures automatic startup using the Windows Startup folder shortcut instead of the Windows Registry.
    /// Cleans up any legacy registry Run keys if present.
    /// </summary>
    public static void SetAutoStart(bool enable)
    {
        try
        {
            // 1. Startup folder shortcut management
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolder, "KerkenezSpeech.lnk");

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KerkenezSpeech.exe");
                }
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                SetupEngine.CreateShortcut(shortcutPath, exePath, "KerkenezSpeech Voice Keyboard", icoPath);
            }
            else
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }

            // 2. Clear legacy registry Run keys if previously set
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (runKey != null)
            {
                runKey.DeleteValue("SpeechRecognation", false);
                runKey.DeleteValue("KerkenezSpeech", false);
            }
        }
        catch { }
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

        string? discovered = SetupEngine.FindLocalNemotronModel();
        if (!string.IsNullOrEmpty(discovered))
        {
            _config.ModelPath = discovered;
            Save();
        }
    }

    public static (string? Encoder, string? Decoder, string? Joiner, string? Tokens) ResolveTransducerFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return (null, null, null, null);

        var onnxFiles = Directory.GetFiles(path, "*.onnx");
        string? encoder = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("encoder", StringComparison.OrdinalIgnoreCase));
        string? decoder = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("decoder", StringComparison.OrdinalIgnoreCase));
        string? joiner = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).StartsWith("joiner", StringComparison.OrdinalIgnoreCase));

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
