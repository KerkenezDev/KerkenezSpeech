using System.Runtime.CompilerServices;
using SherpaOnnx;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.Engine;

public class LazyNemotronEngine : IDisposable
{
    private readonly ConfigService _configService;
    private SherpaOnnxRunner? _runner;
    private readonly object _lock = new();

    public bool IsInitialized => _runner != null;

    public event Action<string>? InterimResultReceived;
    public event Action<string>? UtteranceFinalized;
    public event Action<string>? ErrorOccurred;

    public LazyNemotronEngine(ConfigService configService)
    {
        _configService = configService;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Initialize()
    {
        lock (_lock)
        {
            if (_runner != null) return;

            string modelDir = _configService.Config.ModelPath;
            if (!ConfigService.IsValidModelDir(modelDir))
            {
                throw new DirectoryNotFoundException($"Nemotron model not found at '{modelDir}'.");
            }

            _runner = new SherpaOnnxRunner(modelDir, _configService.Config.NumThreads);
            _runner.InterimResultReceived += text => InterimResultReceived?.Invoke(text);
            _runner.UtteranceFinalized += text => UtteranceFinalized?.Invoke(text);
            _runner.ErrorOccurred += msg => ErrorOccurred?.Invoke(msg);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void StartSession(string languageCode)
    {
        lock (_lock)
        {
            if (_runner == null)
            {
                Initialize();
            }

            _runner!.StartSession(languageCode);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ProcessAudio(float[] samples, int sampleRate)
    {
        lock (_lock)
        {
            _runner?.ProcessAudio(samples, sampleRate);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void EndSession()
    {
        lock (_lock)
        {
            _runner?.EndSession();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _runner?.Dispose();
            _runner = null;
        }
    }

    /// <summary>
    /// Isolated runner class that references SherpaOnnx assemblies.
    /// The CLR will only load sherpa-onnx-c-api.dll and onnxruntime.dll when this class is instantiated.
    /// </summary>
    private class SherpaOnnxRunner : IDisposable
    {
        private OnlineRecognizer? _recognizer;
        private OnlineStream? _currentStream;
        private string _lastReportedText = string.Empty;
        private readonly object _runLock = new();

        public event Action<string>? InterimResultReceived;
        public event Action<string>? UtteranceFinalized;
        public event Action<string>? ErrorOccurred;

        public SherpaOnnxRunner(string modelDir, int numThreads)
        {
            var (encoder, decoder, joiner, tokens) = ConfigService.ResolveTransducerFiles(modelDir);
            if (string.IsNullOrEmpty(encoder) || string.IsNullOrEmpty(decoder) || string.IsNullOrEmpty(joiner) || string.IsNullOrEmpty(tokens))
            {
                throw new FileNotFoundException($"Missing transducer model files (.onnx or tokens.txt) in '{modelDir}'.");
            }

            var config = new OnlineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;

            config.ModelConfig.Transducer.Encoder = encoder;
            config.ModelConfig.Transducer.Decoder = decoder;
            config.ModelConfig.Transducer.Joiner = joiner;
            config.ModelConfig.Tokens = tokens;
            config.ModelConfig.NumThreads = Math.Max(1, numThreads);
            string modelId = ConfigService.DetectModelId(modelDir);
            config.ModelConfig.ModelType = ConfigService.GetModelType(modelId);
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";
            config.EnableEndpoint = 1;
            config.Rule1MinTrailingSilence = 2.4f;
            config.Rule2MinTrailingSilence = 1.0f;
            config.Rule3MinUtteranceLength = 30.0f;

            _recognizer = new OnlineRecognizer(config);
        }

        public void StartSession(string languageCode)
        {
            lock (_runLock)
            {
                if (_recognizer == null) return;

                _currentStream?.Dispose();
                _currentStream = _recognizer.CreateStream();

                if (_currentStream.HasOption("language"))
                {
                    string lang = string.IsNullOrWhiteSpace(languageCode) ? "auto" : languageCode;
                    try
                    {
                        _currentStream.SetOption("language", lang);
                    }
                    catch { }
                }

                _lastReportedText = string.Empty;
            }
        }

        public void ProcessAudio(float[] samples, int sampleRate)
        {
            lock (_runLock)
            {
                if (_recognizer == null || _currentStream == null || samples.Length == 0) return;

                try
                {
                    _currentStream.AcceptWaveform(sampleRate, samples);

                    while (_recognizer.IsReady(_currentStream))
                    {
                        _recognizer.Decode(_currentStream);
                    }

                    bool isEndpoint = _recognizer.IsEndpoint(_currentStream);
                    var result = _recognizer.GetResult(_currentStream);
                    string text = result.Text?.Trim() ?? string.Empty;

                    if (isEndpoint)
                    {
                        if (!string.IsNullOrEmpty(text))
                        {
                            UtteranceFinalized?.Invoke(text);
                        }

                        _recognizer.Reset(_currentStream);
                        _lastReportedText = string.Empty;
                    }
                    else if (!string.IsNullOrEmpty(text) && text != _lastReportedText)
                    {
                        _lastReportedText = text;
                        InterimResultReceived?.Invoke(text);
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"ASR Error: {ex.Message}");
                }
            }
        }

        public void EndSession()
        {
            lock (_runLock)
            {
                if (_recognizer == null || _currentStream == null) return;

                try
                {
                    _currentStream.InputFinished();
                    while (_recognizer.IsReady(_currentStream))
                    {
                        _recognizer.Decode(_currentStream);
                    }

                    var result = _recognizer.GetResult(_currentStream);
                    string text = result.Text?.Trim() ?? string.Empty;

                    if (!string.IsNullOrEmpty(text))
                    {
                        UtteranceFinalized?.Invoke(text);
                    }
                }
                catch { }
                finally
                {
                    _currentStream?.Dispose();
                    _currentStream = null;
                    _lastReportedText = string.Empty;
                }
            }
        }

        public void Dispose()
        {
            lock (_runLock)
            {
                _currentStream?.Dispose();
                _currentStream = null;

                _recognizer?.Dispose();
                _recognizer = null;
            }
        }
    }
}
