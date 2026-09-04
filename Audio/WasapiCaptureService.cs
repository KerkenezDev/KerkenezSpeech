using NAudio.CoreAudioApi;
using NAudio.Wave;
using KerkenezSpeech.Models;
using KerkenezSpeech.Services;

namespace KerkenezSpeech.Audio;

public class WasapiCaptureService : IDisposable
{
    private WasapiCapture? _wasapiCapture;
    private readonly ConfigService _configService;
    private bool _isRecording;
    private readonly object _lock = new();

    public bool IsRecording => _isRecording;

    public event Action<float[], int>? AudioChunkAvailable;
    public event Action<float>? AudioLevelChanged;
    public event Action<string>? ErrorOccurred;

    public WasapiCaptureService(ConfigService configService)
    {
        _configService = configService;
    }

    public static IReadOnlyList<AudioDeviceItem> GetInputDevices()
    {
        var list = new List<AudioDeviceItem>
        {
            new(-1, "Default System Microphone (WASAPI)")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            int idx = 0;
            foreach (var ep in endpoints)
            {
                list.Add(new AudioDeviceItem(idx++, ep.FriendlyName, ep.ID));
            }
        }
        catch { }

        return list;
    }

    public void StartRecording()
    {
        lock (_lock)
        {
            if (_isRecording) return;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? targetDevice = null;

                int deviceIndex = _configService.Config.InputDeviceIndex;
                if (deviceIndex >= 0)
                {
                    var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                    int idx = 0;
                    foreach (var ep in endpoints)
                    {
                        if (idx == deviceIndex)
                        {
                            targetDevice = ep;
                            break;
                        }
                        idx++;
                    }
                }

                targetDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

                var wasapi = new WasapiCapture(targetDevice)
                {
                    ShareMode = AudioClientShareMode.Shared
                };

                var format = wasapi.WaveFormat;
                wasapi.DataAvailable += (s, e) => OnWasapiDataAvailable(e.Buffer, e.BytesRecorded, format);
                wasapi.RecordingStopped += OnRecordingStopped;

                _wasapiCapture = wasapi;
                wasapi.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex)
            {
                _isRecording = false;
                ErrorOccurred?.Invoke($"Microphone capture error: {ex.Message}");
            }
        }
    }

    public void StopRecording()
    {
        WasapiCapture? captureToStop = null;
        lock (_lock)
        {
            if (!_isRecording || _wasapiCapture == null) return;
            _isRecording = false;
            captureToStop = _wasapiCapture;
            _wasapiCapture = null;
        }

        if (captureToStop != null)
        {
            try
            {
                captureToStop.StopRecording();
            }
            catch { }
            finally
            {
                try
                {
                    captureToStop.Dispose();
                }
                catch { }
            }
        }

        AudioLevelChanged?.Invoke(0f);
    }

    private void OnWasapiDataAvailable(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (!_isRecording || bytesRecorded <= 0) return;

        float[] rawSamples = ExtractFloatSamples(buffer, bytesRecorded, format);
        if (rawSamples.Length == 0) return;

        float[] samples16k = ResampleTo16k(rawSamples, format.SampleRate, format.Channels);
        if (samples16k.Length == 0) return;

        double sumSquare = 0;
        for (int i = 0; i < samples16k.Length; i++)
        {
            sumSquare += samples16k[i] * samples16k[i];
        }

        float rms = (float)Math.Sqrt(sumSquare / samples16k.Length);
        float normalizedLevel = Math.Clamp(rms * 5.0f, 0f, 1f);
        AudioLevelChanged?.Invoke(normalizedLevel);

        AudioChunkAvailable?.Invoke(samples16k, 16000);
    }

    public static float[] ExtractFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int floatCount = bytesRecorded / 4;
            float[] samples = new float[floatCount];
            Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
            return samples;
        }
        else if (format.BitsPerSample == 16)
        {
            int sampleCount = bytesRecorded / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = sample / 32768f;
            }
            return samples;
        }
        else if (format.BitsPerSample == 24)
        {
            int sampleCount = bytesRecorded / 3;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int sample = (buffer[i * 3 + 2] << 16) | (buffer[i * 3 + 1] << 8) | buffer[i * 3];
                if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
                samples[i] = sample / 8388608f;
            }
            return samples;
        }
        else if (format.BitsPerSample == 32)
        {
            int sampleCount = bytesRecorded / 4;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int sample = BitConverter.ToInt32(buffer, i * 4);
                samples[i] = sample / 2147483648f;
            }
            return samples;
        }

        return Array.Empty<float>();
    }

    public static float[] ResampleTo16k(float[] inputSamples, int inputRate, int channels)
    {
        if (inputSamples.Length == 0) return Array.Empty<float>();

        float[] mono;
        if (channels > 1)
        {
            int frames = inputSamples.Length / channels;
            mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += inputSamples[i * channels + c];
                }
                mono[i] = sum / channels;
            }
        }
        else
        {
            mono = inputSamples;
        }

        if (inputRate == 16000)
        {
            return mono;
        }

        double ratio = (double)inputRate / 16000.0;
        int outLength = (int)Math.Floor(mono.Length / ratio);
        float[] output = new float[outLength];

        for (int i = 0; i < outLength; i++)
        {
            double srcIdx = i * ratio;
            int idx0 = (int)srcIdx;
            int idx1 = Math.Min(idx0 + 1, mono.Length - 1);
            double frac = srcIdx - idx0;

            output[i] = (float)((1.0 - frac) * mono[idx0] + frac * mono[idx1]);
        }

        return output;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            ErrorOccurred?.Invoke($"WASAPI recording stopped with error: {e.Exception.Message}");
        }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
