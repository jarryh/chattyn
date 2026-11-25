using System.Text.Json;
using Chattyn.Core.Assistant;

namespace Chattyn.Core.Tests;

public class CheckResultTests
{
    private const string EnglishFixture = """
        {
          "input_language": "english",
          "corrected": "/e draws her sword. \"You shouldn't be here.\"",
          "natural": "/e draws her sword. \"You should not be here, stranger.\"",
          "notes": [
            {
              "kind": "grammar",
              "original": "shouldn't to be",
              "suggestion": "shouldn't be",
              "explanation": "Po 'should' nepiš 'to'."
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

    private const string MixedFixture = """
        {
          "input_language": "mixed",
          "corrected": "/e gives him a cold smirk.",
          "natural": "/e gives him a cold smirk.",
          "notes": [
            {
              "kind": "filled_in",
              "original": "úšklebek",
              "suggestion": "smirk",
              "explanation": "Samolibý úsměv. Alternativy: sneer (pohrdavý), grimace (bolestný)."
            }
          ],
          "translations": [
            { "language": "Czech", "original_meaning": "věnuje mu chladný úšklebek.", "natural_meaning": "věnuje mu chladný úšklebek." }
          ]
        }
        """;

    private const string NativeFixture = """
        {
          "input_language": "native",
          "corrected": "/e slowly drew the sword and looked around the tavern.",
          "natural": "/e slowly draws her sword, her gaze sweeping the tavern.",
          "notes": [
            { "kind": "vocabulary", "original": "hospoda", "suggestion": "tavern", "explanation": "Ve fantasy se hodí tavern; inn je hostinec s pokoji." }
          ],
          "translations": [
            { "language": "Czech", "original_meaning": "", "natural_meaning": "pomalu tasí meč a rozhlíží se po hospodě." }
          ]
        }
        """;

    [Fact]
    public void Parse_EnglishPost_ReadsAllFields()
    {
        var result = CheckResult.Parse(EnglishFixture);

        Assert.Equal(InputLanguage.English, result.InputLanguage);
        Assert.StartsWith("/e draws her sword.", result.Corrected);
        Assert.Contains("stranger", result.Natural);
        var note = Assert.Single(result.Notes);
        Assert.Equal("grammar", note.Kind);
        Assert.Equal("shouldn't be", note.Suggestion);

        var czech = Assert.Single(result.Translations);
        Assert.Equal("Czech", czech.Language);
        Assert.True(czech.HasOriginalMeaning);
        Assert.Contains("Neměl bys", czech.OriginalMeaning);
        Assert.Contains("cizinče", czech.NaturalMeaning);
    }

    [Fact]
    public void Parse_MixedPost_HasFilledInNote()
    {
        var result = CheckResult.Parse(MixedFixture);

        Assert.Equal(InputLanguage.Mixed, result.InputLanguage);
        var note = Assert.Single(result.Notes);
        Assert.Equal("filled_in", note.Kind);
        Assert.Equal("filled in", note.KindLabel);
        Assert.Equal("úšklebek", note.Original);
        Assert.Equal("smirk", note.Suggestion);
    }

    [Fact]
    public void Parse_NativePost_HasNoOriginalMeaning()
    {
        var result = CheckResult.Parse(NativeFixture);

        Assert.Equal(InputLanguage.Native, result.InputLanguage);
        Assert.Equal("vocabulary", Assert.Single(result.Notes).Kind);
        var czech = Assert.Single(result.Translations);
        Assert.False(czech.HasOriginalMeaning);
        Assert.Contains("tasí meč", czech.NaturalMeaning);
    }

    [Theory]
    [InlineData("""{"corrected":"Hi.","natural":"Hello."}""")]
    [InlineData("""{"input_language":"klingon","corrected":"Hi.","natural":"Hello."}""")]
    public void Parse_MissingOrUnknownInputLanguage_IsEnglish_AndListsAreEmpty(string json)
    {
        var result = CheckResult.Parse(json);

        Assert.Equal(InputLanguage.English, result.InputLanguage);
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
        Assert.Equal(["input_language", "corrected", "natural", "notes", "translations"], required);

        var inputLanguages = schema["properties"].GetProperty("input_language").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["english", "mixed", "native"], inputLanguages);

        var noteItem = schema["properties"].GetProperty("notes").GetProperty("items");
        Assert.False(noteItem.GetProperty("additionalProperties").GetBoolean());
        var kinds = noteItem.GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("filled_in", kinds);
        Assert.Contains("vocabulary", kinds);

        var translationProps = schema["properties"].GetProperty("translations").GetProperty("items").GetProperty("properties");
        Assert.True(translationProps.TryGetProperty("original_meaning", out _));
        Assert.True(translationProps.TryGetProperty("natural_meaning", out _));
    }
}
