using Chattyn.Core.Assistant;
using Chattyn.Core.Settings;
using Xunit.Abstractions;

namespace Chattyn.Core.Tests;

/// <summary>
/// Runs only when CHATTYN_LIVE_TEST=1: these tests call the real Claude API and cost a little money.
/// The key comes from the ANTHROPIC_API_KEY variable or the repository's .env, like in the app.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CHATTYN_LIVE_TEST") != "1")
            Skip = "Live API test. Set CHATTYN_LIVE_TEST=1 to run it (uses your API credit).";
    }
}

public sealed class LiveApiTests(ITestOutputHelper output) : IDisposable
{
    private static readonly CharacterProfile Aelira = new()
    {
        Name = "Aelira",
        Gender = CharacterGender.She,
        RaceAndClass = "blood elf ranger",
        SpeechStyle = "Formal, cold and precise with strangers. Long, careful sentences. Never uses slang or contractions.",
        Notes = "Proud and distant.",
    };

    private static readonly CharacterProfile Brom = new()
    {
        Name = "Brom",
        Gender = CharacterGender.He,
        RaceAndClass = "dwarf warrior",
        SpeechStyle = "Gruff and blunt. Short sentences, plain words, dwarven dialect (aye, lad, cannae).",
        Notes = "Loud, friendly, loves ale.",
    };

    private static AssistantOptions OptionsFor(CharacterProfile? character) =>
        new(AppSettings.DefaultModel, ["Czech"], "Czech", character, []);

    private readonly ClaudeWritingAssistant _assistant = new(
        () => ApiKeyResolver.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable).Key);

    public void Dispose() => _assistant.Dispose();

    private Task<CheckResult> Check(string post) => Check(post, Aelira);

    private async Task<CheckResult> Check(string post, CharacterProfile? character)
    {
        var result = await _assistant.CheckAsync(post, OptionsFor(character), CancellationToken.None);

        output.WriteLine($"CHARACTER: {character?.Name ?? "(none)"}");
        output.WriteLine($"POST:      {post}");
        output.WriteLine($"LANGUAGE:  {result.InputLanguage}");
        output.WriteLine($"CORRECTED: {result.Corrected}");
        output.WriteLine($"NATURAL:   {result.Natural}");
        foreach (var n in result.Notes)
            output.WriteLine($"NOTE [{n.Kind}] {n.Original} -> {n.Suggestion}: {n.Explanation}");
        foreach (var t in result.Translations)
            output.WriteLine($"{t.Language}: wrote=\"{t.OriginalMeaning}\" natural=\"{t.NaturalMeaning}\"");
        return result;
    }

    private static void AssertEnglish(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain(text, c => "áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ".Contains(c));
    }

    [LiveFact]
    public async Task EnglishWithTypicalCzechMistakes_IsCorrected()
    {
        var result = await Check("/s I have many informations about actual situation in city.");

        Assert.Equal(InputLanguage.English, result.InputLanguage);
        Assert.StartsWith("/s ", result.Corrected);
        Assert.StartsWith("/s ", result.Natural);
        Assert.DoesNotContain("informations", result.Corrected);
        Assert.DoesNotContain("informations", result.Natural);
        Assert.DoesNotContain("actual situation", result.Natural); // false friend: aktuální
        Assert.NotEmpty(result.Notes);

        var czech = Assert.Single(result.Translations);
        Assert.True(czech.HasOriginalMeaning);
        Assert.False(string.IsNullOrWhiteSpace(czech.NaturalMeaning));
    }

    [LiveFact]
    public async Task EnglishWithOneCzechWord_GetsTheWordFilledIn()
    {
        var result = await Check("/e gives him a cold úšklebek and turns away.");

        Assert.Equal(InputLanguage.Mixed, result.InputLanguage);
        Assert.StartsWith("/e ", result.Corrected);
        AssertEnglish(result.Corrected);
        AssertEnglish(result.Natural);
        Assert.Contains(result.Notes, n => n.Kind == "filled_in");
        Assert.All(result.Notes, n => Assert.NotEqual(n.Original.Trim(), n.Suggestion.Trim()));
        Assert.True(Assert.Single(result.Translations).HasOriginalMeaning);
    }

    [LiveFact]
    public async Task CzechPost_IsTranslatedToEnglish()
    {
        var result = await Check("/e pomalu tasila meč a rozhlédla se po hospodě.");

        Assert.Equal(InputLanguage.Native, result.InputLanguage);
        Assert.StartsWith("/e ", result.Corrected);
        Assert.StartsWith("/e ", result.Natural);
        AssertEnglish(result.Corrected);
        AssertEnglish(result.Natural);
        Assert.Contains("sword", result.Corrected); // natural may pick "blade" to fit the character
        Assert.Contains("her", result.Corrected + " " + result.Natural); // gender from "tasila"

        var czech = Assert.Single(result.Translations);
        Assert.False(czech.HasOriginalMeaning); // the post already is Czech
        Assert.False(string.IsNullOrWhiteSpace(czech.NaturalMeaning));
    }

    [LiveFact]
    public async Task SingleCzechWord_IsAVocabularyLookupWithAlternatives()
    {
        var result = await Check("úšklebek");

        Assert.Equal(InputLanguage.Native, result.InputLanguage);
        AssertEnglish(result.Natural);
        Assert.True(result.Natural.Split(' ').Length <= 4, "A lookup should answer with a word or short phrase.");
        Assert.NotEmpty(result.Notes);
    }

    [LiveFact]
    public async Task SameSpokenLine_SoundsDifferentForDifferentCharacters()
    {
        const string post = "/s Hello, I am glad that you came. Sit down and drink with me, we must to talk about the war.";

        var asAelira = await Check(post, Aelira);
        var asBrom = await Check(post, Brom);

        // The voice shapes the natural version...
        Assert.NotEqual(asAelira.Natural, asBrom.Natural);
        Assert.StartsWith("/s ", asAelira.Natural);
        Assert.StartsWith("/s ", asBrom.Natural);
        // ...while corrected only fixes the mistake, whoever speaks.
        Assert.DoesNotContain("must to", asAelira.Corrected);
        Assert.DoesNotContain("must to", asBrom.Corrected);
        Assert.Equal(asAelira.Corrected, asBrom.Corrected);
    }

    [LiveFact]
    public async Task CharacterGender_DecidesPronounsWhenTheOriginalDoesNotShowIt()
    {
        // Czech present tense "tasí" does not show gender.
        const string post = "/e tasí meč.";

        var she = await Check(post, Aelira);
        var he = await Check(post, Brom);

        Assert.Contains("her sword", she.Natural);
        Assert.Contains("his sword", he.Natural);
    }

    [LiveFact]
    public async Task CorrectEnglish_IsLeftUnchanged()
    {
        const string post = "/e nods slowly and sheathes her sword.";
        var result = await Check(post);

        Assert.Equal(InputLanguage.English, result.InputLanguage);
        Assert.Equal(post, result.Corrected.Trim());
    }
}
