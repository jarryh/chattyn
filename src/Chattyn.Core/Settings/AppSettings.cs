using System.Globalization;

namespace Chattyn.Core.Settings;

/// <summary>Which version of the post gets copied for WoW chat.</summary>
public enum PostChoice
{
    Mine,
    Corrected,
    Natural,
}

/// <summary>
/// User preferences, stored as JSON in %APPDATA%\Chattyn\settings.json.
/// Never holds secrets: the API key comes from .env (see <see cref="ApiKeyResolver"/>).
/// </summary>
public sealed class AppSettings
{
    public const string DefaultModel = "claude-haiku-5-5";

    public static readonly IReadOnlyList<string> KnownModels =
    [
        "claude-haiku-5-5",
        "claude-sonnet-5-5",
        "claude-opus-5-5",
    ];

    // Assistant
    public string Model { get; set; } = DefaultModel;
    public List<string> TranslationLanguages { get; set; } = [];
    public string ExplanationLanguage { get; set; } = "English";
    public string CharacterNotes { get; set; } = "";
    public List<string> LoreWords { get; set; } = [];

    // Behaviour
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+Space";
    public string NextPartHotkey { get; set; } = "Ctrl+Alt+N";
    public bool AutoCheck { get; set; }
    public int AutoCheckDelayMs { get; set; } = 1500;
    public PostChoice PreferredChoice { get; set; } = PostChoice.Natural;
    public bool StartWithWindows { get; set; }

    // Sending to chat
    public int ChunkLimitBytes { get; set; } = ChatChunkerDefaults.MaxBytes;
    public bool NumberParts { get; set; } = true;
    public bool NormalizePunctuation { get; set; } = true;

    // Window
    public double IdleOpacity { get; set; } = 0.8;
    public double ActiveOpacity { get; set; } = 1.0;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 540;
    public double ExpandedHeight { get; set; } = 600;

    /// <summary>
    /// First-run defaults. Translation and explanation language follow the Windows display
    /// language when it is not English, so a Czech Windows gets Czech out of the box.
    /// </summary>
    public static AppSettings CreateDefault(CultureInfo uiCulture)
    {
        var settings = new AppSettings();
        var neutral = uiCulture.IsNeutralCulture ? uiCulture : uiCulture.Parent;
        if (!string.IsNullOrEmpty(neutral.Name) && neutral.TwoLetterISOLanguageName != "en")
        {
            settings.TranslationLanguages.Add(neutral.EnglishName);
            settings.ExplanationLanguage = neutral.EnglishName;
        }
        return settings;
    }

    /// <summary>Clamps values a hand-edited settings file could break.</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Model)) Model = DefaultModel;
        if (string.IsNullOrWhiteSpace(ExplanationLanguage)) ExplanationLanguage = "English";
        TranslationLanguages = CleanList(TranslationLanguages);
        LoreWords = CleanList(LoreWords);
        CharacterNotes ??= "";
        ToggleHotkey ??= "Ctrl+Alt+Space";
        NextPartHotkey ??= "Ctrl+Alt+N";
        AutoCheckDelayMs = Math.Clamp(AutoCheckDelayMs, 500, 10_000);
        ChunkLimitBytes = Math.Clamp(ChunkLimitBytes, 50, 4000);
        IdleOpacity = Math.Clamp(IdleOpacity, 0.2, 1.0);
        ActiveOpacity = Math.Clamp(ActiveOpacity, 0.4, 1.0);
        Width = Math.Clamp(Width, 320, 2000);
        ExpandedHeight = Math.Clamp(ExpandedHeight, 260, 2000);
    }

    private static List<string> CleanList(List<string>? items) =>
        (items ?? [])
            .Select(s => s?.Trim() ?? "")
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class ChatChunkerDefaults
{
    /// <summary>WoW rejects chat messages longer than 255 bytes.</summary>
    public const int MaxBytes = 255;
}
