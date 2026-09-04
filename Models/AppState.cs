namespace KerkenezSpeech.Models;

public enum EngineState
{
    Unloaded,
    Loading,
    ActiveListening,
    ReadyCached
}

public enum TypingMode
{
    RealtimeDelta,
    SentenceBySentence
}

public record AudioDeviceItem(int DeviceNumber, string Name, string DeviceId = "");

public class AppConfig
{
    public string Version { get; set; } = "0.1.0";
    public string ModelId { get; set; } = "nemotron-int8";
    public string ModelPath { get; set; } = string.Empty;
    public string ModelName { get; set; } = "Nemotron-3.5-ASR 0.6B Streaming INT8";
    public string LanguageCode { get; set; } = "auto";
    public int InputDeviceIndex { get; set; } = -1; // -1 = Default System Microphone
    public TypingMode TypingMode { get; set; } = TypingMode.RealtimeDelta;
    public bool OpenMicMode { get; set; } = false;
    public string GlobalHotkey { get; set; } = "Win+Alt+V";
    public int NumThreads { get; set; } = Math.Min(4, Environment.ProcessorCount);
    
    /// <summary>
    /// Duration in seconds to keep model in RAM after stopping.
    /// 0 = Unload immediately; 30, 60, 300, 900; -1 = Infinite (Always in RAM).
    /// </summary>
    public int RamCacheSeconds { get; set; } = 60;
    
    public bool AutoStartOnBoot { get; set; } = false;
    public bool AddTrailingSpace { get; set; } = true;
}
