using System.Text.Json;
using Chattyn.Core.Assistant;

namespace Chattyn.Core.Tests;

public class CheckResultTests
{
    private const string Fixture = """
        {
          "corrected": "/e draws her sword. \"You shouldn't be here.\"",
          "natural": "/e draws her sword. \"You should not be here, stranger.\"",
          "notes": [
            {
              "kind": "grammar",
              "original": "shouldn't to be",
              "suggestion": "shouldn't be",
              "explanation": "Po 'should' nepiš 'to'."
            },
            {
              "kind": "filled_in",
              "original": "cizinec",
              "suggestion": "stranger",
              "explanation": "Doplněno anglické slovo."
            }
          ],
          "translations": [
            {
              "language": "Czech",
              "original_meaning": "tasí meč. \"Neměl bys tu být.\"",
              "natural_meaning": "tasí meč. \"Neměl bys tu být, cizinče.\""
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsAllFields()
    {
        var result = CheckResult.Parse(Fixture);

        Assert.StartsWith("/e draws her sword.", result.Corrected);
        Assert.Contains("stranger", result.Natural);
        Assert.Equal(2, result.Notes.Count);
        Assert.Equal("grammar", result.Notes[0].Kind);
        Assert.Equal("shouldn't be", result.Notes[0].Suggestion);
        Assert.Equal("filled in", result.Notes[1].KindLabel);

        var czech = Assert.Single(result.Translations);
        Assert.Equal("Czech", czech.Language);
        Assert.Contains("Neměl bys", czech.OriginalMeaning);
        Assert.Contains("cizinče", czech.NaturalMeaning);
    }

    [Fact]
    public void Parse_MissingListsBecomeEmpty()
    {
        var result = CheckResult.Parse("""{"corrected":"Hi.","natural":"Hello."}""");
        Assert.Empty(result.Notes);
        Assert.Empty(result.Translations);
    }

    [Fact]
    public void Parse_InvalidJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => CheckResult.Parse("not json"));

    [Fact]
    public void Schema_IsStrictObjectWithAllFieldsRequired()
    {
        var schema = CheckResultSchema.Build();

        Assert.Equal("object", schema["type"].GetString());
        Assert.False(schema["additionalProperties"].GetBoolean());
        var required = schema["required"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["corrected", "natural", "notes", "translations"], required);

        var noteItem = schema["properties"].GetProperty("notes").GetProperty("items");
        Assert.False(noteItem.GetProperty("additionalProperties").GetBoolean());
        var kinds = noteItem.GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("filled_in", kinds);

        var translationProps = schema["properties"].GetProperty("translations").GetProperty("items").GetProperty("properties");
        Assert.True(translationProps.TryGetProperty("original_meaning", out _));
        Assert.True(translationProps.TryGetProperty("natural_meaning", out _));
    }
}
