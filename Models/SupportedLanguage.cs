namespace KerkenezSpeech.Models;

public record SupportedLanguage(
    string Code,
    string DisplayName,
    string NativeName,
    string Region,
    bool IsPopular = false
)
{
    public string FullTitle => string.IsNullOrWhiteSpace(NativeName) || NativeName == DisplayName
        ? DisplayName
        : $"{DisplayName} ({NativeName})";

    public static readonly IReadOnlyList<SupportedLanguage> All = new List<SupportedLanguage>
    {
        new("auto", "Auto Detect (Multilingual)", "Automatic Detection", "Global", true),
        new("en-US", "English (United States)", "English (US)", "Americas", true),
        new("en-GB", "English (United Kingdom)", "English (UK)", "Europe", true),
        new("es-ES", "Spanish (Spain)", "Español (España)", "Europe", true),
        new("es-US", "Spanish (Latin America)", "Español (Latinoamérica)", "Americas", true),
        new("fr-FR", "French (France)", "Français", "Europe", true),
        new("fr-CA", "French (Canada)", "Français (Canada)", "Americas", false),
        new("de-DE", "German", "Deutsch", "Europe", true),
        new("it-IT", "Italian", "Italiano", "Europe", true),
        new("pt-BR", "Portuguese (Brazil)", "Português (Brasil)", "Americas", true),
        new("pt-PT", "Portuguese (Portugal)", "Português (Portugal)", "Europe", false),
        new("zh-CN", "Chinese (Simplified)", "中文 (简体)", "Asia", true),
        new("ja-JP", "Japanese", "日本語", "Asia", true),
        new("ko-KR", "Korean", "한국어", "Asia", true),
        new("ar-AR", "Arabic", "العربية", "Middle East", true),
        new("ru-RU", "Russian", "Русский", "Europe/Asia", true),
        new("hi-IN", "Hindi", "हिन्दी", "Asia", true),
        new("tr-TR", "Turkish", "Türkçe", "Europe/Asia", true),
        new("vi-VN", "Vietnamese", "Tiếng Việt", "Asia", true),
        new("uk-UA", "Ukrainian", "Українська", "Europe", false),
        new("pl-PL", "Polish", "Polski", "Europe", false),
        new("nl-NL", "Dutch", "Nederlands", "Europe", false),
        new("sv-SE", "Swedish", "Svenska", "Europe", false),
        new("da-DK", "Danish", "Dansk", "Europe", false),
        new("fi-FI", "Finnish", "Suomi", "Europe", false),
        new("nb-NO", "Norwegian (Bokmål)", "Norsk Bokmål", "Europe", false),
        new("nn-NO", "Norwegian (Nynorsk)", "Norsk Nynorsk", "Europe", false),
        new("cs-CZ", "Czech", "Čeština", "Europe", false),
        new("el-GR", "Greek", "Ελληνικά", "Europe", false),
        new("ro-RO", "Romanian", "Română", "Europe", false),
        new("hu-HU", "Hungarian", "Magyar", "Europe", false),
        new("sk-SK", "Slovak", "Slovenčina", "Europe", false),
        new("sl-SL", "Slovenian", "Slovenščina", "Europe", false),
        new("bg-BG", "Bulgarian", "Български", "Europe", false),
        new("hr-HR", "Croatian", "Hrvatski", "Europe", false),
        new("lt-LT", "Lithuanian", "Lietuvių", "Europe", false),
        new("lv-LV", "Latvian", "Latviešu", "Europe", false),
        new("et-EE", "Estonian", "Eesti", "Europe", false),
        new("he-IL", "Hebrew", "עברית", "Middle East", false),
        new("th-TH", "Thai", "ไทย", "Asia", false)
    };

    public static readonly IReadOnlyList<SupportedLanguage> ZipformerEnglishLanguages = new List<SupportedLanguage>
    {
        new("en-US", "English (United States)", "English (US)", "Americas", true)
    };

    public static readonly IReadOnlyList<SupportedLanguage> ZipformerBilingualLanguages = new List<SupportedLanguage>
    {
        new("auto", "Auto Detect (Chinese / English)", "自动检测", "Global", true),
        new("zh-CN", "Chinese (Simplified)", "中文 (简体)", "Asia", true),
        new("en-US", "English (United States)", "English (US)", "Americas", true)
    };

    public static IReadOnlyList<SupportedLanguage> GetSupportedLanguages(string? modelId)
    {
        return modelId?.ToLowerInvariant() switch
        {
            "zipformer-en" => ZipformerEnglishLanguages,
            "zipformer-bilingual" => ZipformerBilingualLanguages,
            _ => All
        };
    }

    public static string NormalizeLanguageForModel(string? modelId, string? requestedLang)
    {
        var supported = GetSupportedLanguages(modelId);
        if (string.IsNullOrWhiteSpace(requestedLang))
        {
            return supported[0].Code;
        }

        if (supported.Any(l => string.Equals(l.Code, requestedLang, StringComparison.OrdinalIgnoreCase)))
        {
            return requestedLang;
        }

        string shortCode = requestedLang.Split('-')[0].ToLowerInvariant();
        var match = supported.FirstOrDefault(l => l.Code.StartsWith(shortCode, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match.Code;
        }

        return supported[0].Code;
    }

    public static SupportedLanguage FindByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return All[0];

        var match = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        string shortCode = code.Split('-')[0].ToLowerInvariant();
        match = All.FirstOrDefault(l => l.Code.StartsWith(shortCode, StringComparison.OrdinalIgnoreCase));
        return match ?? All[0];
    }
}
