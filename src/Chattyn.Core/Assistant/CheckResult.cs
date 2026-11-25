using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chattyn.Core.Assistant;

/// <summary>What Claude returns for one checked post.</summary>
public sealed record CheckResult(
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
            dto.Corrected ?? "",
            dto.Natural ?? "",
            dto.Notes ?? [],
            dto.Translations ?? []);
    }

    private sealed record Dto(
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

/// <summary>Translation of the writer's own text and of the natural rewrite into one language.</summary>
public sealed record CheckTranslation(string Language, string OriginalMeaning, string NaturalMeaning);

/// <summary>JSON schema sent as the structured output format, so the reply always parses.</summary>
public static class CheckResultSchema
{
    public static readonly IReadOnlyList<string> NoteKinds =
        ["spelling", "grammar", "word_choice", "punctuation", "style", "filled_in"];

    public static Dictionary<string, JsonElement> Build()
    {
        var str = new { type = "string" };
        var schema = new
        {
            type = "object",
            properties = new
            {
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
            required = new[] { "corrected", "natural", "notes", "translations" },
            additionalProperties = false,
        };

        var element = JsonSerializer.SerializeToElement(schema);
        return element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
