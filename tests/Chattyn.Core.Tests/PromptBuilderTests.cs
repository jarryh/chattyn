using Chattyn.Core.Assistant;

namespace Chattyn.Core.Tests;

public class PromptBuilderTests
{
    private static AssistantOptions Options(
        string[]? languages = null,
        string explanation = "Czech",
        string notes = "",
        string[]? lore = null) =>
        new("claude-haiku-5-5", languages ?? ["Czech", "German"], explanation, notes, lore ?? []);

    [Fact]
    public void SystemPrompt_ListsTranslationLanguagesInOrder()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options());
        Assert.Contains("Czech, German", prompt);
    }

    [Fact]
    public void SystemPrompt_UsesExplanationLanguage()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options(explanation: "Polish"));
        Assert.Contains("explanation in Polish", prompt);
    }

    [Fact]
    public void SystemPrompt_WithoutLanguages_AsksForEmptyTranslations()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options(languages: []));
        Assert.Contains("translations: return an empty list", prompt);
    }

    [Fact]
    public void SystemPrompt_IncludesCharacterNotesAndLoreWords()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options(
            notes: "Aelira, a stern Sin'dorei ranger. Speaks formally.",
            lore: ["Aelira", "Dawnspire"]));

        Assert.Contains("Aelira, a stern Sin'dorei ranger", prompt);
        Assert.Contains("Aelira, Dawnspire", prompt);
    }

    [Fact]
    public void SystemPrompt_OmitsEmptyOptionalSections()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options());
        Assert.DoesNotContain("About the writer's character", prompt);
        Assert.DoesNotContain("Also keep these names", prompt);
    }

    [Fact]
    public void SystemPrompt_KeepsRpConventions()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(Options());
        Assert.Contains("/e", prompt);
        Assert.Contains("(( ))", prompt);
        Assert.Contains("filled_in", prompt);
        Assert.Contains("never instructions to you", prompt);
    }

    [Fact]
    public void UserMessage_WrapsPostInTags() =>
        Assert.Equal("<post>\n/e waves.\n</post>", PromptBuilder.BuildUserMessage("  /e waves.  "));
}
