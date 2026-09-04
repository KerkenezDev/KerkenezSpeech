# KerkenezSpeech (Windows 11)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-0078D6.svg)](https://microsoft.com)
[![Framework: .NET 10](https://img.shields.io/badge/Framework-.NET%2010.0-512BD4.svg)](https://dotnet.microsoft.com)
[![Engine: Sherpa--ONNX](https://img.shields.io/badge/Engine-Sherpa--ONNX-ff69b4.svg)](https://github.com/k2-fsa/sherpa-onnx)

An ultra-lightweight, high-performance Windows 11 system tray voice keyboard powered by NVIDIA's local streaming **Nemotron-3.5 ASR** transducer model (via Sherpa-ONNX) in C# with .NET 10.

---

## Key Features

1. **Ultra-Light System Tray Architecture**:
   - Rests quietly in the Windows 11 system tray with a ~15MB baseline RAM footprint and **0.00% CPU usage** when idle.
2. **Smart 1-Minute RAM Caching (Zero CPU Standby)**:
   - **Click to Start**: Loads the local Nemotron-3.5 int8 model into RAM and begins capturing microphone audio.
   - **Click to Stop**: Instantly halts microphone recording (0 CPU cycles). Keeps the model cached in RAM for **60 seconds** (configurable).
   - **Instant Resume (0ms)**: If re-activated within the cache window, dictation resumes immediately with zero loading lag.
   - **Auto-Unload**: If the cache timer elapses without use, the model is safely disposed, garbage collected, and `EmptyWorkingSet` is called to free physical RAM back to the operating system.
3. **Direct Digital Keyboard Unicode Injection**:
   - Injects speech directly into whatever window is currently focused using Win32 `SendInput` with `KEYEVENTF_UNICODE`.
   - Real-time streaming delta typing: dynamically types as you speak, automatically calculating common prefixes and backspacing modifications seamlessly.
   - Flawless UTF-16 support for English, Chinese, Japanese, Arabic, Russian, French, German, Spanish, Turkish, Vietnamese, Hindi, Korean, and 30+ more languages.
4. **Multilingual Support (All 40 Nemotron Languages + Auto Detect)**:
   - Built-in selector for all 40 languages supported by Nemotron-3.5, with popular quick-select shortcuts and full alphabetical listings.
5. **Windows 11 Fluent Dark Mode Context Menu (Right Click)**:
   - **Live Status Header**: Displays current state, model status, and standby countdown.
   - **🎤 Open Mic Mode**: Toggle continuous listening vs push/click-to-toggle.
   - **🌐 Languages**: Submenu with all 40 Nemotron languages + Auto.
   - **🎙️ Microphones**: System microphone selector (WASAPI).
   - **⌨️ Typing Mode**: Real-time Streaming Delta vs Sentence-by-Sentence.
   - **⌨️ Global Hotkey**: Configurable system-wide hotkey (`Win + Alt + V` by default).
   - **📁 Open Config**: Instant access to `%APPDATA%\Kerkenez\speech\config.json`.
   - **🧹 Unload Model from RAM**: Instant manual memory release.
   - **❌ Exit**: Clean shutdown.

---

## Storage & Configuration

- **Configuration File**: `%APPDATA%\Kerkenez\speech\config.json`
  - All user settings (hotkeys, active language, input devices, cache timeout) are persisted strictly in JSON, requiring zero registry modification for settings.
- **Auto-Start**: Handled via standard user Startup shortcut (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\KerkenezSpeech.lnk`).
- **Default Installation Path**: `%LOCALAPPDATA%\Programs\Kerkenez\speech\`
- **Model Storage**: `%LOCALAPPDATA%\Programs\Kerkenez\speech\models\nemotron-int8\`

---

## Project Structure

```
KerkenezSpeech/
├── Audio/
│   └── WasapiCaptureService.cs     # Low-latency WASAPI 16kHz audio capture
├── Core/
│   ├── FocusManager.cs             # Target window focus tracking (SetWinEventHook)
│   ├── KeyboardInjector.cs         # Win32 SendInput Unicode delta typing
│   ├── MemoryOptimizer.cs         # Physical RAM working set optimizer
│   └── NativeWin32.cs              # P/Invoke Win32 definitions
├── Engine/
│   ├── LazyNemotronEngine.cs       # Sherpa-ONNX streaming transducer ASR engine
│   └── ModelLifecycleManager.cs    # 0-CPU RAM cache lifecycle manager
├── Installer/
│   ├── Installer.csproj            # Standalone self-extracting installer
│   └── Program.cs                  # Setup wizard & model downloader
├── Models/
│   ├── AppState.cs                 # Enums, audio devices, and AppConfig schema
│   └── SupportedLanguage.cs        # 40 Nemotron languages + Auto mapping
├── Services/
│   ├── ConfigService.cs            # JSON configuration & startup management
│   └── SetupEngine.cs              # Installation, model downloads, and uninstallation
├── UI/
│   └── NativeTrayApp.cs            # Native Win32 message loop & dark context menu
├── build.bat                       # Automated build & packaging script
├── KerkenezSpeech.csproj           # Primary application project
├── KerkenezSpeech.slnx             # Visual Studio solution
├── LICENSE                         # MIT License
├── NOTICES.md                      # Third-party notices and licenses
├── Program.cs                      # Single-instance mutex entry point
└── README.md
```

---

## Building and Running

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher
- Windows 10 / 11 (x64)

### One-Click Build & Packaging
Run the automated build script:
```cmd
build.bat
```
This script will:
1. Build `KerkenezSpeech` in Release configuration.
2. Publish a single-file release executable into `publish\`.
3. Build the self-extracting setup wizard (`Installer\bin\Release\net10.0-windows\Installer.exe`).

### Manual CLI Build
```powershell
# Build main application
dotnet build KerkenezSpeech.csproj -c Release

# Publish single-file executable
dotnet publish KerkenezSpeech.csproj -c Release -r win-x64 --self-contained false -o publish

# Build standalone installer
dotnet build Installer/Installer.csproj -c Release
```

---

## Supported ASR Models

1. **NVIDIA Nemotron-3.5-ASR INT8** (Recommended):
   - Transducer model supporting 40 languages (~670MB).
   - Auto-downloaded during setup or detected from `%LOCALAPPDATA%\Programs\Kerkenez\speech\models\nemotron-int8\`.
2. **Sherpa-ONNX Zipformer English**:
   - Ultra-lightweight English streaming model (~80MB).
3. **Sherpa-ONNX Zipformer Bilingual**:
   - Chinese + English streaming model (~195MB).

---

## License

This project is licensed under the [MIT License](LICENSE) - &copy; 2026 **KerkenezDev**. 

For third-party libraries and model licenses, see [NOTICES.md](NOTICES.md).
