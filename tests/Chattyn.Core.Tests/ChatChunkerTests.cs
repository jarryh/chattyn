using Chattyn.Core.Text;

namespace Chattyn.Core.Tests;

public class ChatChunkerTests
{
    private const string Sentence = "Aelira draws her sword slowly, the blade catching the last light of the setting sun. ";

    [Fact]
    public void ShortText_IsOnePart()
    {
        var parts = ChatChunker.Split("/e nods.", 255);
        Assert.Equal(["/e nods."], parts);
    }

    [Fact]
    public void EmptyText_HasNoParts()
    {
        Assert.Empty(ChatChunker.Split("   ", 255));
        Assert.Empty(ChatChunker.Split(null, 255));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongText_EveryPartFitsTheByteLimit(bool numbered)
    {
        var text = string.Concat(Enumerable.Repeat(Sentence, 9)).Trim();
        var parts = ChatChunker.Split(text, 255, numbered);

        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.InRange(ChatChunker.Bytes(p), 1, 255));
    }

    [Fact]
    public void LongText_DoesNotSplitInsideWords()
    {
        var text = string.Concat(Enumerable.Repeat(Sentence, 9)).Trim();
        var parts = ChatChunker.Split(text, 255);

        var rejoined = string.Join(" ", parts);
        Assert.Equal(text, rejoined);
        Assert.All(parts, p => Assert.False(p.StartsWith(' ') || p.EndsWith(' ')));
    }

    [Fact]
    public void LongText_PrefersSentenceEnds()
    {
        var text = string.Concat(Enumerable.Repeat(Sentence, 9)).Trim();
        var parts = ChatChunker.Split(text, 255);

        Assert.All(parts, p => Assert.EndsWith(".", p));
    }

    [Fact]
    public void MultiByteCharacters_AreCountedInBytes()
    {
        // "ř" and "ž" are 2 bytes each in UTF-8: 200 chars here are 400 bytes.
        var text = string.Join(" ", Enumerable.Repeat("řž" + new string('a', 6), 25));
        Assert.True(text.Length < 255);
        Assert.True(ChatChunker.Bytes(text) > 255);

        var parts = ChatChunker.Split(text, 255);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.True(ChatChunker.Bytes(p) <= 255));
    }

    [Fact]
    public void EmojiSurrogatePairs_AreNeverCutInHalf()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F5E1", 100)); // 100 daggers, 400 bytes, no spaces
        var parts = ChatChunker.Split(text, 255);

        Assert.All(parts, p =>
        {
            Assert.True(ChatChunker.Bytes(p) <= 255);
            Assert.False(char.IsHighSurrogate(p[^1]));
            Assert.False(char.IsLowSurrogate(p[0]));
        });
        Assert.Equal(text, string.Concat(parts));
    }

    [Fact]
    public void NumberedParts_HaveCounterSuffix()
    {
        var text = string.Concat(Enumerable.Repeat(Sentence, 9)).Trim();
        var parts = ChatChunker.Split(text, 255, numberParts: true);

        for (var i = 0; i < parts.Count; i++)
            Assert.EndsWith($" ({i + 1}/{parts.Count})", parts[i]);
    }

    [Fact]
    public void NumberedParts_WithTenOrMoreParts_StillFit()
    {
        var text = string.Concat(Enumerable.Repeat(Sentence, 40)).Trim();
        var parts = ChatChunker.Split(text, 255, numberParts: true);

        Assert.True(parts.Count >= 10);
        Assert.All(parts, p => Assert.True(ChatChunker.Bytes(p) <= 255));
        Assert.EndsWith($" (10/{parts.Count})", parts[9]);
    }

    [Theory]
    [InlineData("/e ", "/e ")]
    [InlineData("/me ", "/me ")]
    [InlineData("/s ", "/s ")]
    [InlineData("/w Thrall ", "/w Thrall ")]
    [InlineData("/1 ", "/1 ")]
    public void ChatCommand_IsRepeatedOnEveryPart(string command, string expectedPrefix)
    {
        var text = command + string.Concat(Enumerable.Repeat(Sentence, 6)).Trim();
        var parts = ChatChunker.Split(text, 255);

        Assert.True(parts.Count > 1);
        Assert.All(parts, p =>
        {
            Assert.StartsWith(expectedPrefix, p);
            Assert.True(ChatChunker.Bytes(p) <= 255);
        });
    }

    [Fact]
    public void SplitCommandPrefix_IgnoresTextWithoutCommand()
    {
        Assert.Equal(("", "/shrug"), ChatChunker.SplitCommandPrefix("/shrug"));
        Assert.Equal(("", "Hello there"), ChatChunker.SplitCommandPrefix("Hello there"));
        Assert.Equal(("/e ", "waves."), ChatChunker.SplitCommandPrefix("/e   waves."));
    }

    [Fact]
    public void SingleHugeWord_IsHardSplit()
    {
        var text = new string('x', 600);
        var parts = ChatChunker.Split(text, 255);

        Assert.Equal(3, parts.Count);
        Assert.Equal(text, string.Concat(parts));
    }
}
