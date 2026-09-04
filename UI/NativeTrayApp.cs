using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using KerkenezSpeech.Audio;
using KerkenezSpeech.Core;
using KerkenezSpeech.Engine;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.UI;

public class NativeTrayApp : IDisposable
{
    private readonly ConfigService _configService;
    private readonly FocusManager _focusManager;
    private readonly LazyNemotronEngine _nemotronEngine;
    private readonly WasapiCaptureService _audioService;
    private readonly KeyboardInjector _keyboardInjector;
    private readonly ModelLifecycleManager _lifecycleManager;

    private IntPtr _hWnd = IntPtr.Zero;
    private NativeWin32.WndProc? _wndProcDelegate;
    private NativeWin32.NOTIFYICONDATA _nid;
    private IntPtr _currentHIcon = IntPtr.Zero;
    private bool _isExiting = false;

    // Menu Command IDs
    private const uint CMD_TOGGLE = 1001;
    private const uint CMD_OPEN_MIC = 1002;
    private const uint CMD_UNLOAD_RAM = 1003;
    private const uint CMD_EXIT = 1099;
    private const uint CMD_TYPING_DELTA = 1101;
    private const uint CMD_TYPING_SENTENCE = 1102;
    
    // Cache Options Command IDs
    private const uint CMD_CACHE_0 = 1200;
    private const uint CMD_CACHE_30 = 1201;
    private const uint CMD_CACHE_60 = 1202;
    private const uint CMD_CACHE_300 = 1203;
    private const uint CMD_CACHE_900 = 1204;
    private const uint CMD_CACHE_INF = 1205;
    
    // Other Options
    private const uint CMD_OPT_AUTOSTART = 1210;
    private const uint CMD_OPT_TRAILING_SPACE = 1211;
    private const uint CMD_OPT_OPEN_CONFIG = 1212;
    private const uint CMD_OPT_ABOUT = 1213;

    private const uint CMD_LANG_BASE = 2000;
    private const uint CMD_MIC_BASE = 3000;

    public NativeTrayApp()
    {
        // 1. Initialize Services
        _configService = new ConfigService();
        _focusManager = new FocusManager();
        _keyboardInjector = new KeyboardInjector(_configService);
        _audioService = new WasapiCaptureService(_configService);
        _nemotronEngine = new LazyNemotronEngine(_configService);
        _lifecycleManager = new ModelLifecycleManager(_configService, _nemotronEngine, _audioService, _keyboardInjector);

        // 2. Enable Windows 11 Dark Mode for Menus
        try
        {
            NativeWin32.SetPreferredAppMode(2); // 2 = AllowDark / ForceDark
            NativeWin32.FlushMenuThemes();
        }
        catch { }

        // 3. Create Native Win32 Message Window
        CreateMessageWindow();

        // 4. Create System Tray Icon
        CreateTrayIcon();

        // 5. Register Global Hotkey
        RegisterGlobalHotkey();

        // 6. Wire Lifecycle Events
        _lifecycleManager.StateChanged += OnStateChanged;
        _lifecycleManager.StandbyCountdownTick += OnStandbyTick;

        // Maintain continuous sub-1MB working set while idle (matches EmailSummarizer.exe behavior)
        MemoryOptimizer.TrimWorkingSet();
        StartIdleMemoryMaintainer();
    }

    private void StartIdleMemoryMaintainer()
    {
        Task.Run(async () =>
        {
            await Task.Delay(500);
            MemoryOptimizer.TrimWorkingSet();
            await Task.Delay(1200);
            MemoryOptimizer.TrimWorkingSet();

            while (!_isExiting)
            {
                await Task.Delay(5000);
                if (!_isExiting && _lifecycleManager.State != EngineState.ActiveListening && _lifecycleManager.State != EngineState.Loading)
                {
                    MemoryOptimizer.TrimWorkingSet();
                }
            }
        });
    }

    private void CreateMessageWindow()
    {
        string className = "SpeechRecognation_MsgWnd_" + Guid.NewGuid().ToString("N");
        _wndProcDelegate = WindowProc;

        var wndClass = new NativeWin32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeWin32.WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = NativeWin32.GetModuleHandle(null),
            lpszClassName = className
        };

        NativeWin32.RegisterClassEx(ref wndClass);

        _hWnd = NativeWin32.CreateWindowEx(
            0,
            className,
            "KerkenezSpeech",
            0,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeWin32.GetModuleHandle(null),
            IntPtr.Zero
        );
    }

    private void CreateTrayIcon()
    {
        _currentHIcon = RenderMicIcon(EngineState.Unloaded);

        _nid = new NativeWin32.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeWin32.NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NativeWin32.NIF_MESSAGE | NativeWin32.NIF_ICON | NativeWin32.NIF_TIP,
            uCallbackMessage = NativeWin32.WM_TRAYICON,
            hIcon = _currentHIcon,
            szTip = "KerkenezSpeech - Idle"
        };

        NativeWin32.Shell_NotifyIcon(NativeWin32.NIM_ADD, ref _nid);
    }

    private void RegisterGlobalHotkey()
    {
        NativeWin32.RegisterHotKey(_hWnd, 1, NativeWin32.MOD_WIN | NativeWin32.MOD_ALT | NativeWin32.MOD_NOREPEAT, 0x56); // 0x56 = 'V'
    }

    public void Run()
    {
        while (!_isExiting && NativeWin32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeWin32.TranslateMessage(ref msg);
            NativeWin32.DispatchMessage(ref msg);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        switch (uMsg)
        {
            case NativeWin32.WM_TRAYICON:
                uint mouseMsg = (uint)lParam;
                if (mouseMsg == NativeWin32.WM_LBUTTONUP || mouseMsg == NativeWin32.WM_LBUTTONDBLCLK)
                {
                    _focusManager.RestoreTargetFocus();
                    _ = _lifecycleManager.ToggleListeningAsync();
                }
                else if (mouseMsg == NativeWin32.WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                return IntPtr.Zero;

            case NativeWin32.WM_HOTKEY:
                _ = _lifecycleManager.ToggleListeningAsync();
                return IntPtr.Zero;

            case NativeWin32.WM_DESTROY:
                _isExiting = true;
                NativeWin32.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeWin32.DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        NativeWin32.GetCursorPos(out var pt);
        IntPtr hMenu = NativeWin32.CreatePopupMenu();

        // 1. Status Banner
        string statusText = _lifecycleManager.State switch
        {
            EngineState.ActiveListening => $"🟢 Status: Listening [{SupportedLanguage.FindByCode(_configService.Config.LanguageCode).DisplayName.Split('(')[0].Trim()}]",
            EngineState.ReadyCached => _lifecycleManager.RemainingStandbySeconds == -1
                ? "🟡 Status: Ready in RAM (Infinite Cache)"
                : $"🟡 Status: Ready in RAM ({_lifecycleManager.RemainingStandbySeconds}s)",
            EngineState.Loading => "⏳ Status: Loading Model into RAM...",
            _ => "💤 Status: Idle (RAM Free)"
        };
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING | NativeWin32.MF_DISABLED | NativeWin32.MF_GRAYED, UIntPtr.Zero, statusText);
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING | NativeWin32.MF_DISABLED | NativeWin32.MF_GRAYED, UIntPtr.Zero, $"🧠 Model: {_configService.Config.ModelName}");
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);

        // 2. Toggle Start/Stop Dictation
        string toggleText = _lifecycleManager.State == EngineState.ActiveListening ? "⏹️ Stop Dictation" : "🎙️ Start Dictation (Win+Alt+V)";
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING, (UIntPtr)CMD_TOGGLE, toggleText);

        // 3. Open Mic Mode Toggle
        uint openMicFlags = NativeWin32.MF_STRING | (_configService.Config.OpenMicMode ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
        NativeWin32.AppendMenu(hMenu, openMicFlags, (UIntPtr)CMD_OPEN_MIC, "🎤 Open Mic Mode (Continuous)");
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);

        // 4. Languages Submenu (Dynamically filtered by active model)
        var supportedLangs = _configService.GetSupportedLanguagesForActiveModel();
        string activeLangCode = _configService.Config.LanguageCode;

        if (supportedLangs.Count == 1)
        {
            // Single-language model (e.g. zipformer-en only supports English)
            var singleLang = supportedLangs[0];
            NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING | NativeWin32.MF_CHECKED | NativeWin32.MF_GRAYED, (UIntPtr)CMD_LANG_BASE, $"🌐 Language: {singleLang.DisplayName} (English Only)");
        }
        else if (supportedLangs.Count <= 5)
        {
            // Small language set model (e.g. zipformer-bilingual with Chinese & English)
            IntPtr hLangMenu = NativeWin32.CreatePopupMenu();
            for (int i = 0; i < supportedLangs.Count; i++)
            {
                var l = supportedLangs[i];
                uint langFlags = NativeWin32.MF_STRING | (string.Equals(l.Code, activeLangCode, StringComparison.OrdinalIgnoreCase) ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
                NativeWin32.AppendMenu(hLangMenu, langFlags, (UIntPtr)(CMD_LANG_BASE + i), l.FullTitle);
            }
            NativeWin32.AppendMenu(hMenu, NativeWin32.MF_POPUP, (UIntPtr)hLangMenu, $"🌐 Languages ({supportedLangs.Count})");
        }
        else
        {
            // Multilingual model (Nemotron-3.5 with 40 languages)
            IntPtr hLangMenu = NativeWin32.CreatePopupMenu();
            NativeWin32.AppendMenu(hLangMenu, NativeWin32.MF_STRING | NativeWin32.MF_DISABLED | NativeWin32.MF_GRAYED, UIntPtr.Zero, "— Frequent Languages —");
            for (int i = 0; i < supportedLangs.Count; i++)
            {
                var l = supportedLangs[i];
                if (l.IsPopular)
                {
                    uint langFlags = NativeWin32.MF_STRING | (string.Equals(l.Code, activeLangCode, StringComparison.OrdinalIgnoreCase) ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
                    NativeWin32.AppendMenu(hLangMenu, langFlags, (UIntPtr)(CMD_LANG_BASE + i), l.FullTitle);
                }
            }

            NativeWin32.AppendMenu(hLangMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);

            IntPtr hAllLangMenu = NativeWin32.CreatePopupMenu();
            for (int i = 0; i < supportedLangs.Count; i++)
            {
                var l = supportedLangs[i];
                uint langFlags = NativeWin32.MF_STRING | (string.Equals(l.Code, activeLangCode, StringComparison.OrdinalIgnoreCase) ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
                NativeWin32.AppendMenu(hAllLangMenu, langFlags, (UIntPtr)(CMD_LANG_BASE + i), l.FullTitle);
            }
            NativeWin32.AppendMenu(hLangMenu, NativeWin32.MF_POPUP, (UIntPtr)hAllLangMenu, $"All {supportedLangs.Count} Languages (A-Z)");
            NativeWin32.AppendMenu(hMenu, NativeWin32.MF_POPUP, (UIntPtr)hLangMenu, "🌐 Languages");
        }

        // 5. Microphones Submenu (Queried lazily on demand)
        IntPtr hMicMenu = NativeWin32.CreatePopupMenu();
        var devices = WasapiCaptureService.GetInputDevices();
        int activeDeviceIdx = _configService.Config.InputDeviceIndex;

        for (int i = 0; i < devices.Count; i++)
        {
            var dev = devices[i];
            uint devFlags = NativeWin32.MF_STRING | (dev.DeviceNumber == activeDeviceIdx ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
            NativeWin32.AppendMenu(hMicMenu, devFlags, (UIntPtr)(CMD_MIC_BASE + i), dev.Name);
        }
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_POPUP, (UIntPtr)hMicMenu, "🎙️ Microphones");

        // 6. Typing Mode Submenu
        IntPtr hTypingMenu = NativeWin32.CreatePopupMenu();
        uint deltaFlag = NativeWin32.MF_STRING | (_configService.Config.TypingMode == TypingMode.RealtimeDelta ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
        uint sentenceFlag = NativeWin32.MF_STRING | (_configService.Config.TypingMode == TypingMode.SentenceBySentence ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
        NativeWin32.AppendMenu(hTypingMenu, deltaFlag, (UIntPtr)CMD_TYPING_DELTA, "⚡ Real-time Streaming (Live typing)");
        NativeWin32.AppendMenu(hTypingMenu, sentenceFlag, (UIntPtr)CMD_TYPING_SENTENCE, "📝 Sentence Mode (Types on pause)");
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_POPUP, (UIntPtr)hTypingMenu, "⌨️ Typing Mode");

        // 7. Options Submenu
        IntPtr hOptionsMenu = NativeWin32.CreatePopupMenu();

        // 7a. RAM Cache Time Submenu
        IntPtr hCacheMenu = NativeWin32.CreatePopupMenu();
        int currentCache = _configService.Config.RamCacheSeconds;
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == 0 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_0, "0s (Instant Unload - Free RAM immediately)");
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == 30 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_30, "30 Seconds");
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == 60 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_60, "1 Minute (Default)");
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == 300 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_300, "5 Minutes");
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == 900 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_900, "15 Minutes");
        NativeWin32.AppendMenu(hCacheMenu, NativeWin32.MF_STRING | (currentCache == -1 ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED), (UIntPtr)CMD_CACHE_INF, "Infinite (Always in RAM - Instant response)");

        NativeWin32.AppendMenu(hOptionsMenu, NativeWin32.MF_POPUP, (UIntPtr)hCacheMenu, "⏱ Keep Model in RAM");

        // 7b. AutoStart on Boot
        uint autoStartFlags = NativeWin32.MF_STRING | (_configService.Config.AutoStartOnBoot ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
        NativeWin32.AppendMenu(hOptionsMenu, autoStartFlags, (UIntPtr)CMD_OPT_AUTOSTART, "🚀 Start with Windows");

        // 7c. Add Trailing Space
        uint trailingSpaceFlags = NativeWin32.MF_STRING | (_configService.Config.AddTrailingSpace ? NativeWin32.MF_CHECKED : NativeWin32.MF_UNCHECKED);
        NativeWin32.AppendMenu(hOptionsMenu, trailingSpaceFlags, (UIntPtr)CMD_OPT_TRAILING_SPACE, "␣ Add Trailing Space After Dictation");

        NativeWin32.AppendMenu(hOptionsMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeWin32.AppendMenu(hOptionsMenu, NativeWin32.MF_STRING, (UIntPtr)CMD_OPT_OPEN_CONFIG, "📁 Open Config Folder (config.json)");
        NativeWin32.AppendMenu(hOptionsMenu, NativeWin32.MF_STRING, (UIntPtr)CMD_OPT_ABOUT, $"ℹ️ About KerkenezSpeech (v{ConfigService.CurrentVersion})");

        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_POPUP, (UIntPtr)hOptionsMenu, "⚙️ Options");

        // 8. Unload Model Now (if cached in RAM)
        if (_lifecycleManager.State == EngineState.ReadyCached)
        {
            NativeWin32.AppendMenu(hMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);
            NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING, (UIntPtr)CMD_UNLOAD_RAM, "🧹 Unload Model from RAM Now");
        }

        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeWin32.AppendMenu(hMenu, NativeWin32.MF_STRING, (UIntPtr)CMD_EXIT, "❌ Exit");

        // Required before TrackPopupMenu
        NativeWin32.SetForegroundWindow(_hWnd);

        uint cmd = NativeWin32.TrackPopupMenuEx(
            hMenu,
            NativeWin32.TPM_RETURNCMD | NativeWin32.TPM_RIGHTBUTTON | NativeWin32.TPM_NONOTIFY,
            pt.x,
            pt.y,
            _hWnd,
            IntPtr.Zero
        );

        NativeWin32.DestroyMenu(hMenu);

        // Handle Selected Command
        if (cmd == CMD_TOGGLE)
        {
            _focusManager.RestoreTargetFocus();
            _ = _lifecycleManager.ToggleListeningAsync();
        }
        else if (cmd == CMD_OPEN_MIC)
        {
            _configService.UpdateOpenMicMode(!_configService.Config.OpenMicMode);
        }
        else if (cmd == CMD_UNLOAD_RAM)
        {
            _lifecycleManager.UnloadModel();
        }
        else if (cmd == CMD_TYPING_DELTA)
        {
            _configService.UpdateTypingMode(TypingMode.RealtimeDelta);
        }
        else if (cmd == CMD_TYPING_SENTENCE)
        {
            _configService.UpdateTypingMode(TypingMode.SentenceBySentence);
        }
        else if (cmd == CMD_CACHE_0)
        {
            _configService.UpdateRamCacheSeconds(0);
        }
        else if (cmd == CMD_CACHE_30)
        {
            _configService.UpdateRamCacheSeconds(30);
        }
        else if (cmd == CMD_CACHE_60)
        {
            _configService.UpdateRamCacheSeconds(60);
        }
        else if (cmd == CMD_CACHE_300)
        {
            _configService.UpdateRamCacheSeconds(300);
        }
        else if (cmd == CMD_CACHE_900)
        {
            _configService.UpdateRamCacheSeconds(900);
        }
        else if (cmd == CMD_CACHE_INF)
        {
            _configService.UpdateRamCacheSeconds(-1);
        }
        else if (cmd == CMD_OPT_AUTOSTART)
        {
            _configService.UpdateAutoStart(!_configService.Config.AutoStartOnBoot);
        }
        else if (cmd == CMD_OPT_TRAILING_SPACE)
        {
            _configService.UpdateAddTrailingSpace(!_configService.Config.AddTrailingSpace);
        }
        else if (cmd == CMD_OPT_OPEN_CONFIG)
        {
            _configService.OpenConfigFolder();
        }
        else if (cmd == CMD_OPT_ABOUT)
        {
            ShowAboutNotification();
        }
        else if (cmd >= CMD_LANG_BASE && cmd < CMD_MIC_BASE)
        {
            int langIdx = (int)(cmd - CMD_LANG_BASE);
            var supported = _configService.GetSupportedLanguagesForActiveModel();
            if (langIdx >= 0 && langIdx < supported.Count)
            {
                _configService.UpdateLanguage(supported[langIdx].Code);
            }
            else if (langIdx >= 0 && langIdx < SupportedLanguage.All.Count)
            {
                _configService.UpdateLanguage(SupportedLanguage.All[langIdx].Code);
            }
        }
        else if (cmd >= CMD_MIC_BASE && cmd < CMD_MIC_BASE + 100)
        {
            int devIdx = (int)(cmd - CMD_MIC_BASE);
            if (devIdx >= 0 && devIdx < devices.Count)
            {
                _configService.UpdateInputDevice(devices[devIdx].DeviceNumber);
            }
        }
        else if (cmd == CMD_EXIT)
        {
            Exit();
        }
    }

    private void ShowAboutNotification()
    {
        _nid.szInfoTitle = $"KerkenezSpeech v{ConfigService.CurrentVersion}";
        _nid.szInfo = $"Powered by NVIDIA Nemotron-3.5 ASR Streaming Model & Sherpa-ONNX.\nModel: {_configService.Config.ModelName}\nHotkey: {_configService.Config.GlobalHotkey}";
        _nid.dwInfoFlags = NativeWin32.NIIF_INFO;
        _nid.uFlags = NativeWin32.NIF_INFO;
        NativeWin32.Shell_NotifyIcon(NativeWin32.NIM_MODIFY, ref _nid);
    }

    private void OnStateChanged(EngineState state)
    {
        IntPtr oldHIcon = _currentHIcon;
        _currentHIcon = RenderMicIcon(state);

        string langDisplay = SupportedLanguage.FindByCode(_configService.Config.LanguageCode).DisplayName.Split('(')[0].Trim();
        string tip = state switch
        {
            EngineState.ActiveListening => $"KerkenezSpeech: Listening [{langDisplay}]\nClick tray to stop",
            EngineState.ReadyCached => _lifecycleManager.RemainingStandbySeconds == -1
                ? $"KerkenezSpeech: Ready in RAM (Infinite Cache)\nClick tray for instant start"
                : $"KerkenezSpeech: Ready in RAM ({_lifecycleManager.RemainingStandbySeconds}s left)\nClick tray for instant start",
            EngineState.Loading => "KerkenezSpeech: Loading Nemotron Model...",
            _ => "KerkenezSpeech: Idle (RAM free)\nClick tray to start dictation"
        };

        _nid.hIcon = _currentHIcon;
        _nid.szTip = tip;
        _nid.uFlags = NativeWin32.NIF_ICON | NativeWin32.NIF_TIP;
        NativeWin32.Shell_NotifyIcon(NativeWin32.NIM_MODIFY, ref _nid);

        if (oldHIcon != IntPtr.Zero)
        {
            NativeWin32.DestroyIcon(oldHIcon);
        }
    }

    private void OnStandbyTick(int remainingSeconds)
    {
        if (_lifecycleManager.State == EngineState.ReadyCached)
        {
            _nid.szTip = remainingSeconds == -1
                ? $"KerkenezSpeech: Ready in RAM (Infinite Cache)\nClick tray for instant start"
                : $"KerkenezSpeech: Ready in RAM ({remainingSeconds}s left)\nClick tray for instant start";
            _nid.uFlags = NativeWin32.NIF_TIP;
            NativeWin32.Shell_NotifyIcon(NativeWin32.NIM_MODIFY, ref _nid);
        }
    }

    private static IntPtr RenderMicIcon(EngineState state)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float scale = size / 32.0f;
        Color capsuleColor;
        Color cradleColor;
        Color statusDotColor = Color.Transparent;

        switch (state)
        {
            case EngineState.ActiveListening:
                capsuleColor = Color.FromArgb(255, 65, 65);   // Vibrant Red
                cradleColor = Color.FromArgb(255, 110, 110);
                statusDotColor = Color.FromArgb(255, 235, 0);
                break;

            case EngineState.ReadyCached:
                capsuleColor = Color.FromArgb(0, 200, 220);   // Cyan
                cradleColor = Color.FromArgb(80, 210, 230);
                statusDotColor = Color.FromArgb(0, 255, 210);
                break;

            case EngineState.Loading:
                capsuleColor = Color.FromArgb(255, 185, 0);   // Amber
                cradleColor = Color.FromArgb(255, 210, 70);
                statusDotColor = Color.FromArgb(255, 255, 100);
                break;

            case EngineState.Unloaded:
            default:
                // Exact Windows 11 taskbar matching microphone colors
                capsuleColor = Color.FromArgb(215, 222, 230); // Soft Silver-White
                cradleColor = Color.FromArgb(145, 155, 168);  // Slate Gray
                break;
        }

        float capWidth = 10f * scale;
        float capHeight = 14.5f * scale;
        float capX = (size - capWidth) / 2f;
        float capY = 3.5f * scale;

        // Solid Capsule Head
        using (var capBrush = new SolidBrush(capsuleColor))
        {
            using var path = new GraphicsPath();
            float r = capWidth / 2f;
            path.AddArc(capX, capY, capWidth, capWidth, 180, 180);
            path.AddLine(capX + capWidth, capY + r, capX + capWidth, capY + capHeight - r);
            path.AddArc(capX, capY + capHeight - capWidth, capWidth, capWidth, 0, 180);
            path.AddLine(capX, capY + capHeight - r, capX, capY + r);
            path.CloseFigure();
            g.FillPath(capBrush, path);
        }

        // Cradle Arc + Stem + Base
        float strokeWidth = Math.Max(1.5f, 2.0f * scale);
        using (var pen = new Pen(cradleColor, strokeWidth))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;

            float arcX = capX - 3.2f * scale;
            float arcY = capY + 3.0f * scale;
            float arcW = capWidth + 6.4f * scale;
            float arcH = capHeight + 1.5f * scale;

            g.DrawArc(pen, arcX, arcY, arcW, arcH, 0, 180);

            float stemTop = arcY + arcH / 2f + arcH / 2f;
            float stemBottom = stemTop + 5.5f * scale;
            float centerX = size / 2f;
            g.DrawLine(pen, centerX, stemTop - 0.5f * scale, centerX, stemBottom);

            float baseHalfWidth = 5.5f * scale;
            g.DrawLine(pen, centerX - baseHalfWidth, stemBottom, centerX + baseHalfWidth, stemBottom);
        }

        // Status indicator dot
        if (statusDotColor != Color.Transparent)
        {
            using var dotBrush = new SolidBrush(statusDotColor);
            using var dotBorder = new Pen(Color.FromArgb(30, 30, 30), 1f);
            g.FillEllipse(dotBrush, size - 8 * scale, 2 * scale, 6 * scale, 6 * scale);
            g.DrawEllipse(dotBorder, size - 8 * scale, 2 * scale, 6 * scale, 6 * scale);
        }

        return bmp.GetHicon();
    }

    public void Exit()
    {
        _isExiting = true;
        NativeWin32.Shell_NotifyIcon(NativeWin32.NIM_DELETE, ref _nid);
        NativeWin32.UnregisterHotKey(_hWnd, 1);
        NativeWin32.DestroyWindow(_hWnd);
        Dispose();
    }

    public void Dispose()
    {
        _focusManager.Dispose();
        _lifecycleManager.Dispose();

        if (_currentHIcon != IntPtr.Zero)
        {
            NativeWin32.DestroyIcon(_currentHIcon);
            _currentHIcon = IntPtr.Zero;
        }
    }
}
