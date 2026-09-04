using System.Diagnostics;
using System.Text.Json;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.Installer;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = $"KerkenezSpeech Setup - v{SetupEngine.AppVersion}";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Check for --uninstall
        if (args.Length > 0 && args[0].ToLowerInvariant() is "--uninstall" or "-u" or "/uninstall")
        {
            bool quiet = args.Contains("--quiet") || args.Contains("-q");
            SetupEngine.Uninstall(quiet);
            return;
        }

        PrintHeader();

        // 1. Check existing installation & version
        string? existingVersion = SetupEngine.GetInstalledVersion();
        string? existingLocation = SetupEngine.GetInstalledLocation();

        if (!string.IsNullOrEmpty(existingVersion))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[i] Found existing installation: v{existingVersion} at '{existingLocation}'.");
            Console.WriteLine($"    Upgrading to v{SetupEngine.AppVersion} while preserving your custom configurations.\n");
            Console.ResetColor();
        }

        // 2. Select Installation Directory
        string defaultDir = !string.IsNullOrEmpty(existingLocation) ? existingLocation : SetupEngine.GetDefaultInstallDir();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Target Installation Directory: [{defaultDir}]");
        Console.ResetColor();
        Console.Write("Press ENTER to accept default or enter custom path: ");
        string? inputDir = Console.ReadLine();
        string targetDir = string.IsNullOrWhiteSpace(inputDir) ? defaultDir : Path.GetFullPath(inputDir.Trim());

        Directory.CreateDirectory(targetDir);

        // 3. Model Selection
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n================== ASR AI MODEL SELECTION ==================");
        Console.ResetColor();

        // Dynamically detect local models in system and user directories
        string? localNemotron = SetupEngine.FindLocalNemotronModel();
        bool hasLocalNemotron = !string.IsNullOrEmpty(localNemotron);

        Console.WriteLine("Choose which Speech Recognition Model to configure:");
        Console.ForegroundColor = ConsoleColor.White;
        if (hasLocalNemotron)
        {
            Console.WriteLine($"  [1] (RECOMMENDED) NVIDIA Nemotron-3.5-ASR INT8 [Found Locally]");
            Console.WriteLine($"      -> Path: {localNemotron}");
        }
        else
        {
            Console.WriteLine("  [1] (RECOMMENDED) NVIDIA Nemotron-3.5-ASR INT8 (~670MB, 40 Languages)");
            Console.WriteLine("      -> Download from HuggingFace");
        }

        Console.WriteLine("  [2] Sherpa-ONNX Zipformer English (~150MB Lightweight Streaming)");
        Console.WriteLine("  [3] Sherpa-ONNX Zipformer Bilingual (Chinese / English, ~160MB)");
        Console.WriteLine("  [4] Use Custom Local Model Directory");
        Console.ResetColor();

        Console.Write("\nSelect Option [1-4] (Default: 1): ");
        string? modelChoice = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(modelChoice)) modelChoice = "1";

        string chosenModelPath = localNemotron ?? Path.Combine(targetDir, "models", "nemotron-int8");
        string chosenModelName = "Nemotron-3.5-ASR 0.6B Streaming INT8";
        string chosenModelId = "nemotron-int8";

        if (modelChoice == "1")
        {
            if (hasLocalNemotron)
            {
                chosenModelPath = localNemotron!;
            }
            else
            {
                string downloadDir = Path.Combine(targetDir, "models", "nemotron-int8");
                var preset = SetupEngine.AvailableModels[0];
                Console.WriteLine($"\nStarting download for {preset.Name}...");
                bool ok = await SetupEngine.DownloadModelAsync(preset, downloadDir, (msg, prog) =>
                {
                    Console.Write($"\r[DL] {msg.PadRight(60)}");
                });
                Console.WriteLine();
                if (ok) chosenModelPath = downloadDir;
            }
        }
        else if (modelChoice == "2")
        {
            var preset = SetupEngine.AvailableModels[1];
            chosenModelId = preset.Id;
            chosenModelName = preset.Name;
            string downloadDir = Path.Combine(targetDir, "models", preset.Id);
            Console.WriteLine($"\nStarting download for {preset.Name}...");
            bool ok = await SetupEngine.DownloadModelAsync(preset, downloadDir, (msg, prog) =>
            {
                Console.Write($"\r[DL] {msg.PadRight(60)}");
            });
            Console.WriteLine();
            if (ok) chosenModelPath = downloadDir;
        }
        else if (modelChoice == "3")
        {
            var preset = SetupEngine.AvailableModels[2];
            chosenModelId = preset.Id;
            chosenModelName = preset.Name;
            string downloadDir = Path.Combine(targetDir, "models", preset.Id);
            Console.WriteLine($"\nStarting download for {preset.Name}...");
            bool ok = await SetupEngine.DownloadModelAsync(preset, downloadDir, (msg, prog) =>
            {
                Console.Write($"\r[DL] {msg.PadRight(60)}");
            });
            Console.WriteLine();
            if (ok) chosenModelPath = downloadDir;
        }
        else if (modelChoice == "4")
        {
            Console.Write("Enter absolute directory path to your model: ");
            string? customPath = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
            {
                chosenModelPath = customPath;
                chosenModelId = ConfigService.DetectModelId(customPath);
                chosenModelName = "Custom Local Transducer Model";
            }
        }

        // 4. Shortcut Options
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n================== SHORTCUTS & STARTUP ==================");
        Console.ResetColor();

        Console.Write("Create Start Menu Shortcut? [Y/n]: ");
        bool createStartMenu = !string.Equals(Console.ReadLine()?.Trim(), "n", StringComparison.OrdinalIgnoreCase);

        Console.Write("Create Desktop Shortcut? [Y/n]: ");
        bool createDesktop = !string.Equals(Console.ReadLine()?.Trim(), "n", StringComparison.OrdinalIgnoreCase);

        Console.Write("Automatically Start with Windows? [y/N]: ");
        bool autoStart = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

        // 5. Install Files
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[1/4] Installing application binaries to '{targetDir}'...");
        Console.ResetColor();

        SetupEngine.KillRunningInstances();

        string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
        string exeName = "KerkenezSpeech.exe";
        string sourceExe = Path.Combine(sourceDir, exeName);
        string targetExe = Path.Combine(targetDir, exeName);
        string targetIco = Path.Combine(targetDir, "app.ico");

        // 1. Deploy Executable (from local disk or embedded payload)
        bool exeDeployed = false;
        if (File.Exists(sourceExe))
        {
            try { File.Copy(sourceExe, targetExe, true); exeDeployed = true; } catch { }
        }
        if (!exeDeployed)
        {
            exeDeployed = ExtractEmbeddedFile("KerkenezSpeech.exe", targetExe);
        }

        // 2. Deploy Icon
        bool icoDeployed = false;
        string sourceIco = Path.Combine(sourceDir, "app.ico");
        if (File.Exists(sourceIco))
        {
            try { File.Copy(sourceIco, targetIco, true); icoDeployed = true; } catch { }
        }
        if (!icoDeployed)
        {
            ExtractEmbeddedFile("app.ico", targetIco);
        }

        // 3. Deploy Native DLLs if present beside installer
        foreach (var dll in new[] { "onnxruntime.dll", "sherpa-onnx-c-api.dll" })
        {
            string sDll = Path.Combine(sourceDir, dll);
            if (File.Exists(sDll))
            {
                try { File.Copy(sDll, Path.Combine(targetDir, dll), true); } catch { }
            }
        }

        // 6. Write / Update config.json
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[2/4] Initializing user configuration (%APPDATA%\\Kerkenez\\speech\\config.json)...");
        Console.ResetColor();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configFolder = Path.Combine(appData, "Kerkenez", "speech");
        Directory.CreateDirectory(configFolder);
        string configPath = Path.Combine(configFolder, "config.json");

        AppConfig cfg = new AppConfig();
        if (File.Exists(configPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath));
                if (loaded != null) cfg = loaded;
            }
            catch { }
        }

        cfg.Version = SetupEngine.AppVersion;
        cfg.ModelId = chosenModelId;
        cfg.ModelPath = chosenModelPath;
        cfg.ModelName = chosenModelName;
        cfg.LanguageCode = SupportedLanguage.NormalizeLanguageForModel(chosenModelId, cfg.LanguageCode);
        cfg.AutoStartOnBoot = autoStart;

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, JsonSerializer.Serialize(cfg, jsonOptions));

        // 7. Register in Windows Programs & Configure AutoStart in HKCU Run
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[3/4] Registering Windows Add or Remove Programs entry & HKCU Run startup...");
        Console.ResetColor();

        SetupEngine.RegisterUninstall(targetDir, targetExe, targetIco);
        ConfigService.SetAutoStart(autoStart);

        // 8. Create Shortcuts
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[4/4] Creating system shortcuts...");
        Console.ResetColor();

        SetupEngine.CreateShortcuts(targetExe, targetIco, createDesktop, createStartMenu);

        // Done!
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n===============================================================");
        Console.WriteLine($"   [✓] SUCCESS: KerkenezSpeech v{SetupEngine.AppVersion} is installed!");
        Console.WriteLine("===============================================================");
        Console.ResetColor();
        Console.WriteLine($"\n• Location: {targetDir}");
        Console.WriteLine($"• Active Model: {chosenModelName}");
        Console.WriteLine($"• Global Hotkey: Win+Alt+V");
        Console.WriteLine($"• Uninstall via: Windows Settings -> Installed Apps, or 'KerkenezSpeech.exe --uninstall'\n");

        Console.Write("Launch KerkenezSpeech now? [Y/n]: ");
        string? launchChoice = Console.ReadLine();
        if (!string.Equals(launchChoice?.Trim(), "n", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            });
            Console.WriteLine("[✓] KerkenezSpeech launched in the system tray!");
        }

        Console.WriteLine("\nPress any key to exit installer...");
        try { Console.ReadKey(); } catch { }
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(@"
    __ __           __                                ____                             __  
   / //_/__  ____  / /_____  ____  ___  ____         / __/___  ___  ___  _____ / /_   
  / ,< / _ \/ __ \/ //_/ _ \/ __ \/ _ \/_  / ______ _\ \ / _ \/ _ \/ -_)/ ___// _ \  
 /_/|_|\___/_/ /_/_/   \___/_/ /_/\___/ /___/_____//___// .__/  __/\__//_/   /_//_/  
                                                       /_/                            
");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  KerkenezSpeech Installer - Version {SetupEngine.AppVersion}");
        Console.WriteLine($"  Author: {SetupEngine.AppAuthor}");
        Console.WriteLine("  Ultra-Light Windows 11 Voice Keyboard powered by NVIDIA Nemotron-3.5");
        Console.WriteLine("========================================================================\n");
        Console.ResetColor();
    }

    private static bool ExtractEmbeddedFile(string endsWithPattern, string destinationPath)
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var resName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(endsWithPattern, StringComparison.OrdinalIgnoreCase));
            if (resName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fs);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}
