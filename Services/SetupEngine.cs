using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using KerkenezSpeech.Models;

namespace KerkenezSpeech.Services;

public record ModelPreset(
    string Id,
    string Name,
    string Description,
    string HuggingFaceBaseUrl,
    string[] Files,
    bool IsRecommended = false
);

public static class SetupEngine
{
    public const string AppName = "KerkenezSpeech";
    public const string AppVersion = ConfigService.CurrentVersion; // "0.1.0"
    public const string AppAuthor = "KerkenezDev";
    public const string RegUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KerkenezSpeech";

    public static readonly IReadOnlyList<ModelPreset> AvailableModels = new List<ModelPreset>
    {
        new(
            "nemotron-int8",
            "NVIDIA Nemotron-3.5-ASR 0.6B Streaming INT8 (Recommended)",
            "Ultra-accurate streaming transducer model supporting 40 languages. Optimized INT8 precision (~670MB).",
            "https://huggingface.co/csukuangfj2/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11/resolve/main",
            new[] { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" },
            true
        ),
        new(
            "zipformer-en",
            "Sherpa-ONNX Zipformer English Streaming (~75MB Lightweight)",
            "Fast, ultra-lightweight English-only streaming transducer (~75MB INT8).",
            "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-en-2023-06-26/resolve/main",
            new[] { "encoder-epoch-99-avg-1-chunk-16-left-128.int8.onnx", "decoder-epoch-99-avg-1-chunk-16-left-128.onnx", "joiner-epoch-99-avg-1-chunk-16-left-128.int8.onnx", "tokens.txt" },
            false
        ),
        new(
            "zipformer-bilingual",
            "Sherpa-ONNX Zipformer Bilingual (ZH / EN) Streaming (~195MB)",
            "Fast streaming transducer for English and Chinese (~195MB INT8).",
            "https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20/resolve/main",
            new[] { "encoder-epoch-99-avg-1.int8.onnx", "decoder-epoch-99-avg-1.onnx", "joiner-epoch-99-avg-1.int8.onnx", "tokens.txt" },
            false
        )
    };

    public static string? FindLocalNemotronModel()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string[] candidates = {
            Path.Combine(localAppData, "Programs", "Kerkenez", "speech", "models", "nemotron-int8"),
            Path.Combine(localAppData, "Programs", "Kerkenez", "speech", "nemotron-int8"),
            Path.Combine(localAppData, "Programs", "Kerkenez", "speech"),
            Path.Combine(baseDir, "models", "nemotron-int8"),
            Path.Combine(baseDir, "aiModels", "nemotron-int8"),
            Path.Combine(baseDir, "nemotron-int8"),
            Path.Combine(appData, "Kerkenez", "speech", "models", "nemotron-int8"),
            Path.Combine(userProfile, "Programs", "ProgramFiles", "aiModels", "nemotron-int8"),
            Path.Combine(userProfile, "aiModels", "nemotron-int8"),
            Path.Combine(userProfile, "Downloads", "nemotron-int8"),
            Path.Combine(userProfile, "Downloads", "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11"),
            @"C:\aiModels\nemotron-int8",
            @"D:\aiModels\nemotron-int8"
        };

        foreach (var candidate in candidates)
        {
            if (ConfigService.IsValidModelDir(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string GetDefaultInstallDir()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", "Kerkenez", "speech");
    }

    public static string? GetInstalledVersion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegUninstallKey);
            return key?.GetValue("DisplayVersion") as string;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetInstalledLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegUninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    public static void RegisterUninstall(string installDir, string exePath, string icoPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegUninstallKey);
            if (key == null) return;

            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", AppVersion);
            key.SetValue("Publisher", AppAuthor);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", File.Exists(icoPath) ? icoPath : exePath);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 110000, RegistryValueKind.DWord); // ~110MB
        }
        catch { }
    }

    public static void UnregisterUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegUninstallKey, false);
            // Also clean legacy key if present
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpeechRecognation", false);
        }
        catch { }
    }

    public static void CreateShortcut(string shortcutPath, string targetPath, string description, string? iconPath = null)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = description;
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                shortcut.IconLocation = iconPath;
            }
            shortcut.Save();
        }
        catch { }
    }

    public static void CreateShortcuts(string exePath, string icoPath, bool createDesktop, bool createStartMenu)
    {
        if (createStartMenu)
        {
            string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KerkenezSpeech");
            Directory.CreateDirectory(startMenuFolder);
            string linkPath = Path.Combine(startMenuFolder, "KerkenezSpeech.lnk");
            CreateShortcut(linkPath, exePath, "KerkenezSpeech Voice Keyboard", icoPath);
        }

        if (createDesktop)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string linkPath = Path.Combine(desktop, "KerkenezSpeech.lnk");
            CreateShortcut(linkPath, exePath, "KerkenezSpeech Voice Keyboard", icoPath);
        }
    }

    public static void RemoveShortcuts()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string desktopLink = Path.Combine(desktop, "KerkenezSpeech.lnk");
            if (File.Exists(desktopLink)) File.Delete(desktopLink);
            string oldDesktopLink = Path.Combine(desktop, "Speech Recognation.lnk");
            if (File.Exists(oldDesktopLink)) File.Delete(oldDesktopLink);

            string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KerkenezSpeech");
            if (Directory.Exists(startMenuFolder))
            {
                Directory.Delete(startMenuFolder, true);
            }
            else
            {
                string singleLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KerkenezSpeech.lnk");
                if (File.Exists(singleLnk)) File.Delete(singleLnk);
            }

            // Legacy start menu cleanup
            string oldStartMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Speech Recognation");
            if (Directory.Exists(oldStartMenuFolder)) Directory.Delete(oldStartMenuFolder, true);
        }
        catch { }
    }

    public static void KillRunningInstances()
    {
        try
        {
            var currentPid = Environment.ProcessId;
            foreach (var procName in new[] { "KerkenezSpeech", "Speech Recognation" })
            {
                foreach (var proc in Process.GetProcessesByName(procName))
                {
                    if (proc.Id != currentPid)
                    {
                        try { proc.Kill(); proc.WaitForExit(2000); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    public static async Task<bool> DownloadModelAsync(ModelPreset preset, string destinationDir, Action<string, double>? progressCallback = null)
    {
        try
        {
            Directory.CreateDirectory(destinationDir);
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);

            for (int i = 0; i < preset.Files.Length; i++)
            {
                string fileName = preset.Files[i];
                string fileUrl = $"{preset.HuggingFaceBaseUrl}/{fileName}";
                string targetPath = Path.Combine(destinationDir, fileName);

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    progressCallback?.Invoke($"File {fileName} already exists. Skipping...", (double)(i + 1) / preset.Files.Length);
                    continue;
                }

                progressCallback?.Invoke($"Downloading {fileName}...", (double)i / preset.Files.Length);

                using var response = await http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        double fileFraction = (double)totalRead / totalBytes.Value;
                        double overall = ((double)i + fileFraction) / preset.Files.Length;
                        progressCallback?.Invoke($"Downloading {fileName} ({totalRead / (1024 * 1024)}MB / {totalBytes.Value / (1024 * 1024)}MB)...", overall);
                    }
                }
            }

            progressCallback?.Invoke("Model download complete!", 1.0);
            return true;
        }
        catch (Exception ex)
        {
            progressCallback?.Invoke($"Download failed: {ex.Message}", 0);
            return false;
        }
    }

    /// <summary>
    /// Completely removes everything associated with KerkenezSpeech:
    /// - Terminate running processes
    /// - Remove Startup shortcut
    /// - Unregister Windows Uninstall
    /// - Delete Desktop & Start Menu Shortcuts
    /// - Delete %APPDATA%\Kerkenez\speech (configs)
    /// - Delete %LOCALAPPDATA%\Programs\Kerkenez\speech (binaries & models)
    /// - Clean up .NET single-file extraction temp folders
    /// </summary>
    public static void Uninstall(bool isQuiet = false)
    {
        KillRunningInstances();

        // 1. Remove AutoStart shortcut
        ConfigService.SetAutoStart(false);

        // 2. Remove Shortcuts
        RemoveShortcuts();

        // 3. Unregister Windows Add/Remove Programs
        UnregisterUninstall();

        // 4. Gather all directories to delete
        var dirsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // User Config Folder (Roaming AppData)
        string roamingConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kerkenez", "speech");
        dirsToDelete.Add(roamingConfig);
        string oldRoamingConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Speech Recognation");
        dirsToDelete.Add(oldRoamingConfig);

        // Installation directory
        string? installDir = GetInstalledLocation();
        if (!string.IsNullOrEmpty(installDir)) dirsToDelete.Add(installDir);
        dirsToDelete.Add(GetDefaultInstallDir());
        string oldInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Speech Recognation");
        dirsToDelete.Add(oldInstallDir);

        // LocalAppData folder
        string localAppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kerkenez", "speech");
        dirsToDelete.Add(localAppDataFolder);
        string oldLocalAppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Speech Recognation");
        dirsToDelete.Add(oldLocalAppDataFolder);

        // Temp .net single-file extract folders
        try
        {
            string tempDir = Path.GetTempPath();
            string netTempDir = Path.Combine(tempDir, ".net");
            if (Directory.Exists(netTempDir))
            {
                foreach (var dir in Directory.GetDirectories(netTempDir, "*KerkenezSpeech*", SearchOption.TopDirectoryOnly))
                {
                    dirsToDelete.Add(dir);
                }
                foreach (var dir in Directory.GetDirectories(netTempDir, "*Speech Recognation*", SearchOption.TopDirectoryOnly))
                {
                    dirsToDelete.Add(dir);
                }
                foreach (var dir in Directory.GetDirectories(netTempDir, "Installer*", SearchOption.TopDirectoryOnly))
                {
                    dirsToDelete.Add(dir);
                }
            }

            string localTempNet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", ".net");
            if (Directory.Exists(localTempNet))
            {
                foreach (var dir in Directory.GetDirectories(localTempNet, "*KerkenezSpeech*", SearchOption.TopDirectoryOnly))
                {
                    dirsToDelete.Add(dir);
                }
                foreach (var dir in Directory.GetDirectories(localTempNet, "*Speech Recognation*", SearchOption.TopDirectoryOnly))
                {
                    dirsToDelete.Add(dir);
                }
            }
        }
        catch { }

        // 5. Build delayed clean-up script in background
        List<string> rmdirCommands = new();
        foreach (var dir in dirsToDelete)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                rmdirCommands.Add($"rmdir /s /q \"{dir}\" 2>NUL");
            }
        }

        string fullCmd = $"/c timeout /t 1 /nobreak > NUL & " + string.Join(" & ", rmdirCommands);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = fullCmd,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
        catch { }

        // Also attempt direct immediate deletion where possible
        foreach (var dir in dirsToDelete)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }

        if (!isQuiet)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✓] KerkenezSpeech (including all configs, shortcuts, and temporary files) has been completely removed from your system.");
            Console.ResetColor();
        }
    }
}
