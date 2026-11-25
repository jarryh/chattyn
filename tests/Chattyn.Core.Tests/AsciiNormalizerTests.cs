using Chattyn.Core.Text;

namespace Chattyn.Core.Tests;

public class AsciiNormalizerTests
{
    [Theory]
    [InlineData("“Hello,” she said.", "\"Hello,\" she said.")]
    [InlineData("It’s fine", "It's fine")]
    [InlineData("Wait…", "Wait...")]
    [InlineData("sword—gleaming", "sword - gleaming")]
    [InlineData("sword — gleaming", "sword - gleaming")]
    [InlineData("1–2", "1 - 2")]
    [InlineData("non‑breaking", "non-breaking")]
    public void TypographicPunctuation_BecomesAscii(string input, string expected) =>
        Assert.Equal(expected, AsciiNormalizer.Normalize(input));

    [Fact]
    public void LineBreaksAndExtraSpaces_CollapseToOneLine() =>
        Assert.Equal("/e nods. \"Fine.\"", AsciiNormalizer.Normalize("  /e nods.\r\n\r\n   \"Fine.\"  "));

    [Fact]
    public void InvisibleCharacters_AreRemoved() =>
        Assert.Equal("Sindorei", AsciiNormalizer.Normalize("Sin​do­rei"));

    [Fact]
    public void LettersWithAccents_AreKept() =>
        Assert.Equal("Quel'Thalas, Dalaran, Žofie, Müller", AsciiNormalizer.Normalize("Quel’Thalas, Dalaran, Žofie, Müller"));

    [Fact]
    public void NullOrEmpty_GivesEmpty()
    {
        Assert.Equal("", AsciiNormalizer.Normalize(null));
        Assert.Equal("", AsciiNormalizer.Normalize(""));
    }
}
