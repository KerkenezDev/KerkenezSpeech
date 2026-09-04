using KerkenezSpeech.Audio;
using KerkenezSpeech.Core;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.Engine;

public class ModelLifecycleManager : IDisposable
{
    private readonly ConfigService _configService;
    private readonly LazyNemotronEngine _nemotronEngine;
    private readonly WasapiCaptureService _audioService;
    private readonly KeyboardInjector _keyboardInjector;

    private EngineState _state = EngineState.Unloaded;
    private CancellationTokenSource? _standbyCts;
    private DateTime? _standbyExpiresAt;
    private readonly object _stateLock = new();

    public EngineState State => _state;
    public DateTime? StandbyExpiresAt => _standbyExpiresAt;
    public int RemainingStandbySeconds
    {
        get
        {
            if (_configService.Config.RamCacheSeconds == -1) return -1; // Infinite
            return _standbyExpiresAt.HasValue
                ? Math.Max(0, (int)(_standbyExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
                : 0;
        }
    }

    public event Action<EngineState>? StateChanged;
    public event Action<int>? StandbyCountdownTick;
    public event Action<string>? ErrorOccurred;

    public ModelLifecycleManager(
        ConfigService configService,
        LazyNemotronEngine nemotronEngine,
        WasapiCaptureService audioService,
        KeyboardInjector keyboardInjector)
    {
        _configService = configService;
        _nemotronEngine = nemotronEngine;
        _audioService = audioService;
        _keyboardInjector = keyboardInjector;

        _nemotronEngine.InterimResultReceived += OnInterimResult;
        _nemotronEngine.UtteranceFinalized += OnUtteranceFinalized;
        _nemotronEngine.ErrorOccurred += msg => ErrorOccurred?.Invoke(msg);
        _audioService.ErrorOccurred += msg => ErrorOccurred?.Invoke(msg);
    }

    public async Task ToggleListeningAsync()
    {
        if (_state == EngineState.ActiveListening)
        {
            StopListening();
        }
        else
        {
            await StartListeningAsync();
        }
    }

    public async Task StartListeningAsync()
    {
        lock (_stateLock)
        {
            if (_state == EngineState.ActiveListening || _state == EngineState.Loading)
            {
                return;
            }

            CancelStandbyTimer();
        }

        try
        {
            if (!_nemotronEngine.IsInitialized)
            {
                SetState(EngineState.Loading);
                await Task.Run(() => _nemotronEngine.Initialize());
            }

            lock (_stateLock)
            {
                string lang = _configService.Config.LanguageCode;
                _nemotronEngine.StartSession(lang);

                _audioService.AudioChunkAvailable += OnAudioChunkAvailable;
                _audioService.StartRecording();

                SetState(EngineState.ActiveListening);
            }
        }
        catch (Exception ex)
        {
            StopListening();
            ErrorOccurred?.Invoke($"Failed to start listening: {ex.Message}");
        }
    }

    public void StopListening()
    {
        lock (_stateLock)
        {
            if (_state != EngineState.ActiveListening && _state != EngineState.Loading)
            {
                return;
            }

            // Immediately halt microphone capture (0 CPU cycles)
            _audioService.AudioChunkAvailable -= OnAudioChunkAvailable;
            _audioService.StopRecording();

            try
            {
                _nemotronEngine.EndSession();
            }
            catch { }

            _keyboardInjector.FinalizeUtterance();

            int cacheSeconds = _configService.Config.RamCacheSeconds;
            if (cacheSeconds == 0)
            {
                // Unload immediately
                UnloadModel();
            }
            else
            {
                // Cache in RAM for configured duration or indefinitely
                SetState(EngineState.ReadyCached);
                StartStandbyTimer();
            }
        }
    }

    private void StartStandbyTimer()
    {
        CancelStandbyTimer();

        int cacheSeconds = _configService.Config.RamCacheSeconds;
        if (cacheSeconds == -1)
        {
            // Infinite standby
            _standbyExpiresAt = null;
            return;
        }

        _standbyExpiresAt = DateTime.UtcNow.AddSeconds(cacheSeconds);
        _standbyCts = new CancellationTokenSource();
        var token = _standbyCts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);

                    if (token.IsCancellationRequested) break;

                    int remaining = RemainingStandbySeconds;
                    StandbyCountdownTick?.Invoke(remaining);

                    if (remaining <= 0)
                    {
                        UnloadModel();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void UnloadModel()
    {
        lock (_stateLock)
        {
            if (_state == EngineState.ActiveListening) return;

            CancelStandbyTimer();
            _standbyExpiresAt = null;

            _nemotronEngine.Dispose();

            // Purge physical working set to baseline (~1.0MB - 2.5MB)
            MemoryOptimizer.TrimWorkingSet();

            SetState(EngineState.Unloaded);
        }
    }

    private void CancelStandbyTimer()
    {
        if (_standbyCts != null)
        {
            try
            {
                _standbyCts.Cancel();
                _standbyCts.Dispose();
            }
            catch { }
            _standbyCts = null;
        }
        _standbyExpiresAt = null;
    }

    private void OnAudioChunkAvailable(float[] samples, int sampleRate)
    {
        if (_state == EngineState.ActiveListening)
        {
            _nemotronEngine.ProcessAudio(samples, sampleRate);
        }
    }

    private void OnInterimResult(string interimText)
    {
        if (_state == EngineState.ActiveListening)
        {
            _keyboardInjector.ProcessStreamingText(interimText);
        }
    }

    private void OnUtteranceFinalized(string finalText)
    {
        _keyboardInjector.FinalizeUtterance(finalText);
    }

    private void SetState(EngineState newState)
    {
        _state = newState;
        StateChanged?.Invoke(_state);
    }

    public void Dispose()
    {
        CancelStandbyTimer();
        _audioService.AudioChunkAvailable -= OnAudioChunkAvailable;
        _audioService.Dispose();
        _nemotronEngine.Dispose();
    }
}
