using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chattyn.Core.Assistant;

/// <summary>How the checked post was written.</summary>
public enum InputLanguage
{
    /// <summary>In English, mistakes included.</summary>
    English,

    /// <summary>In English with some words left in the writer's own language.</summary>
    Mixed,

    /// <summary>Entirely or mostly in the writer's own language; the result is a translation.</summary>
    Native,
}

/// <summary>What Claude returns for one checked post.</summary>
public sealed record CheckResult(
    InputLanguage InputLanguage,
    string Corrected,
    string Natural,
    IReadOnlyList<CheckNote> Notes,
    IReadOnlyList<CheckTranslation> Translations)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parses the JSON text Claude produced under <see cref="CheckResultSchema"/>.</summary>
    public static CheckResult Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions)
                  ?? throw new JsonException("Empty response.");
        return new CheckResult(
            ParseInputLanguage(dto.InputLanguage),
            dto.Corrected ?? "",
            dto.Natural ?? "",
            dto.Notes ?? [],
            dto.Translations ?? []);
    }

    private static InputLanguage ParseInputLanguage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "mixed" => InputLanguage.Mixed,
        "native" => InputLanguage.Native,
        _ => InputLanguage.English,
    };

    private sealed record Dto(
        string? InputLanguage,
        string? Corrected,
        string? Natural,
        List<CheckNote>? Notes,
        List<CheckTranslation>? Translations);
}

/// <summary>One explained correction. <see cref="Kind"/> is one of <see cref="CheckResultSchema.NoteKinds"/>.</summary>
public sealed record CheckNote(string Kind, string Original, string Suggestion, string Explanation)
{
    [JsonIgnore]
    public string KindLabel => Kind.Replace('_', ' ');
}

/// <summary>
/// The post's meaning in one of the writer's languages: what they wrote and what the natural
/// version says. <see cref="OriginalMeaning"/> is empty when the post was already in that language.
/// </summary>
public sealed record CheckTranslation(string Language, string OriginalMeaning, string NaturalMeaning)
{
    [JsonIgnore]
    public bool HasOriginalMeaning => !string.IsNullOrWhiteSpace(OriginalMeaning);
}

/// <summary>JSON schema sent as the structured output format, so the reply always parses.</summary>
public static class CheckResultSchema
{
    public static readonly IReadOnlyList<string> InputLanguages = ["english", "mixed", "native"];

    public static readonly IReadOnlyList<string> NoteKinds =
        ["spelling", "grammar", "word_choice", "punctuation", "style", "filled_in", "vocabulary"];

    public static Dictionary<string, JsonElement> Build()
    {
        var str = new { type = "string" };
        var schema = new
        {
            type = "object",
            properties = new
            {
                input_language = new { type = "string", @enum = InputLanguages },
                corrected = str,
                natural = str,
                notes = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            kind = new { type = "string", @enum = NoteKinds },
                            original = str,
                            suggestion = str,
                            explanation = str,
                        },
                        required = new[] { "kind", "original", "suggestion", "explanation" },
                        additionalProperties = false,
                    },
                },
                translations = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            language = str,
                            original_meaning = str,
                            natural_meaning = str,
                        },
                        required = new[] { "language", "original_meaning", "natural_meaning" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "input_language", "corrected", "natural", "notes", "translations" },
            additionalProperties = false,
        };

        var element = JsonSerializer.SerializeToElement(schema);
        return element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
