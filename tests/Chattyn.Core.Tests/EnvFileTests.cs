using Chattyn.Core.Settings;

namespace Chattyn.Core.Tests;

public sealed class EnvFileTests : IDisposable
{
    private const string FakeKey = "sk-ant-test-0000000000001234";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chattyn-tests-" + Guid.NewGuid().ToString("N"));

    public EnvFileTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Parse_HandlesCommentsQuotesSpacesAndExport()
    {
        var values = EnvFile.Parse("""
            # comment
            ANTHROPIC_API_KEY = sk-ant-plain   # trailing comment

            QUOTED="value with # hash"
            SINGLE='single'
            export EXPORTED=yes
            EMPTY=
            not a pair
            """);

        Assert.Equal("sk-ant-plain", values["ANTHROPIC_API_KEY"]);
        Assert.Equal("value with # hash", values["QUOTED"]);
        Assert.Equal("single", values["SINGLE"]);
        Assert.Equal("yes", values["EXPORTED"]);
        Assert.Equal("", values["EMPTY"]);
        Assert.Equal(5, values.Count);
    }

    [Fact]
    public void Parse_HandlesBomAndWindowsLineEndings()
    {
        var values = EnvFile.Parse("﻿ANTHROPIC_API_KEY=abc\r\nOTHER=1\r\n");
        Assert.Equal("abc", values["ANTHROPIC_API_KEY"]);
        Assert.Equal("1", values["OTHER"]);
    }

    [Fact]
    public void Resolve_FindsEnvInParentFolder()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), $"ANTHROPIC_API_KEY={FakeKey}\n");
        var exeDir = Directory.CreateDirectory(Path.Combine(_root, "src", "bin", "Debug")).FullName;

        var lookup = ApiKeyResolver.Resolve(exeDir, _ => null);

        Assert.True(lookup.Found);
        Assert.Equal(FakeKey, lookup.Key);
        Assert.Equal(Path.Combine(_root, ".env"), lookup.Source);
    }

    [Fact]
    public void Resolve_EnvironmentVariableWins()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), $"ANTHROPIC_API_KEY={FakeKey}\n");

        var lookup = ApiKeyResolver.Resolve(_root, name => name == "ANTHROPIC_API_KEY" ? "sk-ant-from-env" : null);

        Assert.Equal("sk-ant-from-env", lookup.Key);
        Assert.Contains("environment variable", lookup.Source);
    }

    [Fact]
    public void Resolve_EmptyValue_IsNotFoundButReportsFile()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "ANTHROPIC_API_KEY=\n");

        var lookup = ApiKeyResolver.Resolve(_root, _ => null);

        Assert.False(lookup.Found);
        Assert.Equal(Path.Combine(_root, ".env"), lookup.Source);
    }

    [Fact]
    public void Lookup_NeverPrintsTheKey()
    {
        var lookup = new ApiKeyLookup(FakeKey, "somewhere");

        Assert.DoesNotContain(FakeKey, lookup.ToString());
        Assert.Equal("...1234", lookup.MaskedKey);
    }

    [Fact]
    public void SuggestedEnvDirectory_IsNextToEnvExample()
    {
        File.WriteAllText(Path.Combine(_root, ".env.example"), "ANTHROPIC_API_KEY=\n");
        var exeDir = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;

        Assert.Equal(_root, ApiKeyResolver.SuggestedEnvDirectory(exeDir));
    }
}
