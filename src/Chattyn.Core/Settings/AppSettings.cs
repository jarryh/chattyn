using System.Text.Json.Serialization;

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
    public const int CurrentVersion = 3;
    public const string DefaultModel = "claude-haiku-5-5";
    public const string DefaultLanguage = "Czech";
    public const string DefaultToggleHotkey = "Ctrl+Alt+Space";
    public const string DefaultNextPartHotkey = "Ctrl+Alt+PageDown";

    public static readonly IReadOnlyList<string> KnownModels =
    [
        "claude-haiku-5-5",
        "claude-sonnet-5-5",
        "claude-opus-5-5",
    ];

    /// <summary>
    /// Format version of the saved file. The initializer is the oldest version on purpose:
    /// a file saved before this field existed then loads as version 1 and gets migrated.
    /// </summary>
    public int SettingsVersion { get; set; } = 1;

    // Assistant
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// The writer's own language(s): the meaning check is shown in them, text written in them
    /// is translated to English, and their typical mistakes in English are watched for.
    /// </summary>
    public List<string> TranslationLanguages { get; set; } = [DefaultLanguage];

    public string ExplanationLanguage { get; set; } = DefaultLanguage;

    /// <summary>The user's roleplay characters. May be empty: characters are optional.</summary>
    public List<CharacterProfile> Characters { get; set; } = [];

    /// <summary>Id of the character posts are currently written for, or null for "no character".</summary>
    public string? ActiveCharacterId { get; set; }

    /// <summary>
    /// Versions 1 and 2 had a single free-text character field. Kept only so an old file can be
    /// read; <see cref="Migrate"/> turns it into a <see cref="CharacterProfile"/> and clears it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CharacterNotes { get; set; }

    public List<string> LoreWords { get; set; } = [];

    [JsonIgnore]
    public CharacterProfile? ActiveCharacter =>
        ActiveCharacterId is null ? null : Characters.FirstOrDefault(c => c.Id == ActiveCharacterId);

    // Behaviour
    public string ToggleHotkey { get; set; } = DefaultToggleHotkey;
    public string NextPartHotkey { get; set; } = DefaultNextPartHotkey;
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

    /// <summary>Settings for a first run.</summary>
    public static AppSettings CreateDefault() => new() { SettingsVersion = CurrentVersion };

    /// <summary>
    /// One-time upgrade of a file saved by an older version. Version 1 took its languages from the
    /// Windows display language, which left them empty / English on an English Windows, and its
    /// next-part hotkey (Ctrl+Alt+N) blocked AltGr+N on keyboards where AltGr types a character.
    /// </summary>
    /// <remarks>Version 3 replaced the single character text with a list of character profiles.</remarks>
    public void Migrate()
    {
        if (SettingsVersion >= CurrentVersion) return;

        if (SettingsVersion < 2)
        {
            if (TranslationLanguages is null || TranslationLanguages.All(string.IsNullOrWhiteSpace))
                TranslationLanguages = [DefaultLanguage];
            if (string.IsNullOrWhiteSpace(ExplanationLanguage) ||
                ExplanationLanguage.Trim().Equals("English", StringComparison.OrdinalIgnoreCase))
                ExplanationLanguage = DefaultLanguage;
            if (string.Equals(NextPartHotkey?.Replace(" ", ""), "Ctrl+Alt+N", StringComparison.OrdinalIgnoreCase))
                NextPartHotkey = DefaultNextPartHotkey;
        }

        if (SettingsVersion < 3)
        {
            Characters ??= [];
            if (!string.IsNullOrWhiteSpace(CharacterNotes) && Characters.Count == 0)
            {
                var character = new CharacterProfile { Name = "My character", Notes = CharacterNotes.Trim() };
                Characters.Add(character);
                ActiveCharacterId = character.Id;
            }
        }

        CharacterNotes = null;
        SettingsVersion = CurrentVersion;
    }

    /// <summary>Clamps values a hand-edited settings file could break.</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Model)) Model = DefaultModel;
        if (string.IsNullOrWhiteSpace(ExplanationLanguage)) ExplanationLanguage = DefaultLanguage;
        TranslationLanguages = CleanList(TranslationLanguages);
        LoreWords = CleanList(LoreWords);
        CharacterNotes = null;

        Characters = (Characters ?? []).Where(c => c is not null).ToList();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id) || !seenIds.Add(character.Id))
            {
                character.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(character.Id);
            }
            character.Name = character.Name?.Trim() ?? "";
            character.RaceAndClass = character.RaceAndClass?.Trim() ?? "";
            character.SpeechStyle = character.SpeechStyle?.Trim() ?? "";
            character.Notes = character.Notes?.Trim() ?? "";
        }
        if (ActiveCharacter is null) ActiveCharacterId = null;
        if (string.IsNullOrWhiteSpace(ToggleHotkey)) ToggleHotkey = DefaultToggleHotkey;
        if (string.IsNullOrWhiteSpace(NextPartHotkey)) NextPartHotkey = DefaultNextPartHotkey;
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
