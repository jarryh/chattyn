using System.Globalization;
using Chattyn.Core.Settings;

namespace Chattyn.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chattyn-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = new AppSettings
        {
            Model = "claude-sonnet-5-5",
            TranslationLanguages = ["Czech", "German"],
            PreferredChoice = PostChoice.Corrected,
            Left = 100,
            Top = 50,
        };

        store.Save(settings);
        var (loaded, isFirstRun) = store.Load();

        Assert.False(isFirstRun);
        Assert.Equal("claude-sonnet-5-5", loaded.Model);
        Assert.Equal(["Czech", "German"], loaded.TranslationLanguages);
        Assert.Equal(PostChoice.Corrected, loaded.PreferredChoice);
        Assert.Equal(100, loaded.Left);
    }

    [Fact]
    public void MissingFile_IsFirstRun()
    {
        var (settings, isFirstRun) = new SettingsStore(Path.Combine(_dir, "settings.json")).Load();
        Assert.True(isFirstRun);
        Assert.Equal(AppSettings.DefaultModel, settings.Model);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndDefaultsReturned()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        var (settings, _) = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.DefaultModel, settings.Model);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Defaults_FollowNonEnglishWindowsLanguage()
    {
        var czech = AppSettings.CreateDefault(CultureInfo.GetCultureInfo("cs-CZ"));
        Assert.Equal(["Czech"], czech.TranslationLanguages);
        Assert.Equal("Czech", czech.ExplanationLanguage);

        var english = AppSettings.CreateDefault(CultureInfo.GetCultureInfo("en-US"));
        Assert.Empty(english.TranslationLanguages);
        Assert.Equal("English", english.ExplanationLanguage);
    }

    [Fact]
    public void Normalize_ClampsAndCleansValues()
    {
        var settings = new AppSettings
        {
            ChunkLimitBytes = 5,
            IdleOpacity = 0,
            TranslationLanguages = ["  Czech ", "", "czech", "German"],
            Model = " ",
        };

        settings.Normalize();

        Assert.Equal(50, settings.ChunkLimitBytes);
        Assert.Equal(0.2, settings.IdleOpacity);
        Assert.Equal(["Czech", "German"], settings.TranslationLanguages);
        Assert.Equal(AppSettings.DefaultModel, settings.Model);
    }
}
