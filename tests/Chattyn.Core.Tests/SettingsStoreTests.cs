using Chattyn.Core.Settings;

namespace Chattyn.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chattyn-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private AppSettings LoadFromJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, json);
        return new SettingsStore(SettingsPath).Load().Settings;
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = AppSettings.CreateDefault();
        settings.Model = "claude-sonnet-5-5";
        settings.TranslationLanguages = ["Czech", "German"];
        settings.PreferredChoice = PostChoice.Corrected;
        settings.Left = 100;
        settings.Top = 50;

        store.Save(settings);
        var (loaded, isFirstRun) = store.Load();

        Assert.False(isFirstRun);
        Assert.Equal("claude-sonnet-5-5", loaded.Model);
        Assert.Equal(["Czech", "German"], loaded.TranslationLanguages);
        Assert.Equal(PostChoice.Corrected, loaded.PreferredChoice);
        Assert.Equal(100, loaded.Left);
        Assert.Equal(AppSettings.CurrentVersion, loaded.SettingsVersion);
    }

    [Fact]
    public void MissingFile_IsFirstRun_WithCzechDefaults()
    {
        var (settings, isFirstRun) = new SettingsStore(SettingsPath).Load();

        Assert.True(isFirstRun);
        Assert.Equal(AppSettings.DefaultModel, settings.Model);
        Assert.Equal(["Czech"], settings.TranslationLanguages);
        Assert.Equal("Czech", settings.ExplanationLanguage);
        Assert.Equal("Ctrl+Alt+PageDown", settings.NextPartHotkey);
        Assert.Equal(AppSettings.CurrentVersion, settings.SettingsVersion);
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndDefaultsReturned()
    {
        var settings = LoadFromJson("{ this is not json");

        Assert.Equal(AppSettings.DefaultModel, settings.Model);
        Assert.True(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void Version1File_IsMigratedToCzechAndNewHotkey()
    {
        // What version 1 wrote on an English Windows: no languages, English explanations.
        var settings = LoadFromJson("""
            {
              "Model": "claude-haiku-5-5",
              "TranslationLanguages": [],
              "ExplanationLanguage": "English",
              "CharacterNotes": "A stern ranger.",
              "NextPartHotkey": "Ctrl+Alt+N",
              "IdleOpacity": 0.6
            }
            """);

        Assert.Equal(["Czech"], settings.TranslationLanguages);
        Assert.Equal("Czech", settings.ExplanationLanguage);
        Assert.Equal("Ctrl+Alt+PageDown", settings.NextPartHotkey);
        Assert.Equal(AppSettings.CurrentVersion, settings.SettingsVersion);
        // Everything else the user had stays; the old character text became a character.
        Assert.Equal(0.6, settings.IdleOpacity);
        var character = Assert.Single(settings.Characters);
        Assert.Equal("A stern ranger.", character.Notes);
        Assert.Equal(character.Id, settings.ActiveCharacterId);
    }

    [Fact]
    public void Version2File_CharacterNotesBecomeAnActiveCharacter()
    {
        var settings = LoadFromJson("""
            {
              "SettingsVersion": 2,
              "TranslationLanguages": ["Czech"],
              "CharacterNotes": "Aelira, a proud ranger."
            }
            """);

        var character = Assert.Single(settings.Characters);
        Assert.Equal("My character", character.Name);
        Assert.Equal("Aelira, a proud ranger.", character.Notes);
        Assert.Same(character, settings.ActiveCharacter);
        Assert.Null(settings.CharacterNotes);

        // The old field is gone from the file once it is saved again.
        new SettingsStore(SettingsPath).Save(settings);
        Assert.DoesNotContain("CharacterNotes", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Version2File_WithoutCharacterNotes_HasNoCharacters()
    {
        var settings = LoadFromJson("""{ "SettingsVersion": 2, "CharacterNotes": "   " }""");

        Assert.Empty(settings.Characters);
        Assert.Null(settings.ActiveCharacterId);
        Assert.Equal(AppSettings.CurrentVersion, settings.SettingsVersion);
    }

    [Fact]
    public void Characters_RoundTrip_AndKeepTheActiveOne()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = AppSettings.CreateDefault();
        var aelira = new CharacterProfile { Name = "Aelira", Gender = CharacterGender.She, RaceAndClass = "blood elf ranger", SpeechStyle = "Formal.", Notes = "Proud." };
        var brom = new CharacterProfile { Name = "Brom", Gender = CharacterGender.He, SpeechStyle = "Gruff." };
        settings.Characters = [aelira, brom];
        settings.ActiveCharacterId = brom.Id;

        store.Save(settings);
        var loaded = store.Load().Settings;

        Assert.Equal(["Aelira", "Brom"], loaded.Characters.Select(c => c.Name));
        Assert.Equal(CharacterGender.She, loaded.Characters[0].Gender);
        Assert.Equal("blood elf ranger", loaded.Characters[0].RaceAndClass);
        Assert.Equal("Brom", loaded.ActiveCharacter?.Name);
    }

    [Fact]
    public void Normalize_DropsAnActiveCharacterThatNoLongerExists_AndFixesDuplicateIds()
    {
        var settings = AppSettings.CreateDefault();
        settings.Characters =
        [
            new CharacterProfile { Id = "same", Name = "  Aelira  " },
            new CharacterProfile { Id = "same", Name = "Brom" },
        ];
        settings.ActiveCharacterId = "deleted-character";

        settings.Normalize();

        Assert.Null(settings.ActiveCharacterId);
        Assert.Equal("Aelira", settings.Characters[0].Name);
        Assert.NotEqual(settings.Characters[0].Id, settings.Characters[1].Id);
    }

    [Fact]
    public void Version1File_KeepsLanguagesAndHotkeyTheUserChose()
    {
        var settings = LoadFromJson("""
            {
              "TranslationLanguages": ["German"],
              "ExplanationLanguage": "German",
              "NextPartHotkey": "Ctrl+Alt+F8"
            }
            """);

        Assert.Equal(["German"], settings.TranslationLanguages);
        Assert.Equal("German", settings.ExplanationLanguage);
        Assert.Equal("Ctrl+Alt+F8", settings.NextPartHotkey);
    }

    [Fact]
    public void CurrentVersionFile_IsNotMigratedAgain()
    {
        // After the upgrade, an empty list and English explanations are the user's own choice.
        var settings = LoadFromJson("""
            {
              "SettingsVersion": 3,
              "TranslationLanguages": [],
              "ExplanationLanguage": "English",
              "NextPartHotkey": "Ctrl+Alt+N"
            }
            """);

        Assert.Empty(settings.TranslationLanguages);
        Assert.Equal("English", settings.ExplanationLanguage);
        Assert.Equal("Ctrl+Alt+N", settings.NextPartHotkey);
    }

    [Fact]
    public void UnreadableFile_Throws_SoItIsNeverOverwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{}");
        using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => new SettingsStore(SettingsPath).Load());
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
            NextPartHotkey = "",
        };

        settings.Normalize();

        Assert.Equal(50, settings.ChunkLimitBytes);
        Assert.Equal(0.2, settings.IdleOpacity);
        Assert.Equal(["Czech", "German"], settings.TranslationLanguages);
        Assert.Equal(AppSettings.DefaultModel, settings.Model);
        Assert.Equal(AppSettings.DefaultNextPartHotkey, settings.NextPartHotkey);
    }
}
