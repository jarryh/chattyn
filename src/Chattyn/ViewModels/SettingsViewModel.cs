using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Chattyn.Core.Settings;
using Chattyn.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chattyn.ViewModels;

/// <summary>Editable copy of the settings; written back to <see cref="AppSettings"/> only on Save.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Func<ApiKeyLookup> _reloadApiKey;

    public SettingsViewModel(AppSettings settings, ApiKeyLookup apiKey, Func<ApiKeyLookup> reloadApiKey)
    {
        _settings = settings;
        _reloadApiKey = reloadApiKey;

        _model = settings.Model;
        _translationLanguages = string.Join(", ", settings.TranslationLanguages);
        _explanationLanguage = settings.ExplanationLanguage;
        foreach (var character in settings.Characters)
            Characters.Add(new CharacterEditViewModel(character));
        _selectedCharacter = Characters.FirstOrDefault(c => c.Id == settings.ActiveCharacterId) ?? Characters.FirstOrDefault();
        _loreWords = string.Join(Environment.NewLine, settings.LoreWords);
        _toggleHotkey = settings.ToggleHotkey;
        _nextPartHotkey = settings.NextPartHotkey;
        _autoCheck = settings.AutoCheck;
        _autoCheckDelaySeconds = settings.AutoCheckDelayMs / 1000.0;
        _chunkLimitBytes = settings.ChunkLimitBytes.ToString();
        _numberParts = settings.NumberParts;
        _normalizePunctuation = settings.NormalizePunctuation;
        _idleOpacity = settings.IdleOpacity;
        _activeOpacity = settings.ActiveOpacity;
        _startWithWindows = settings.StartWithWindows;
        UpdateKeyStatus(apiKey);
    }

    public IReadOnlyList<string> KnownModels => AppSettings.KnownModels;

    [ObservableProperty] private string _model;
    [ObservableProperty] private string _translationLanguages;
    [ObservableProperty] private string _explanationLanguage;
    [ObservableProperty] private string _loreWords;

    /// <summary>Editable copies of the user's characters.</summary>
    public ObservableCollection<CharacterEditViewModel> Characters { get; } = [];

    /// <summary>The character whose fields are shown in the form, or null when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCharacterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCharacterCommand))]
    private CharacterEditViewModel? _selectedCharacter;

    public bool HasCharacter => SelectedCharacter is not null;

    [RelayCommand]
    private void AddCharacter()
    {
        var character = new CharacterEditViewModel(new CharacterProfile { Name = "New character" });
        Characters.Add(character);
        SelectedCharacter = character;
    }

    [RelayCommand(CanExecute = nameof(HasCharacter))]
    private void DuplicateCharacter()
    {
        if (SelectedCharacter is null) return;
        var copy = SelectedCharacter.ToProfile();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = copy.DisplayName + " (copy)";
        var character = new CharacterEditViewModel(copy);
        Characters.Insert(Characters.IndexOf(SelectedCharacter) + 1, character);
        SelectedCharacter = character;
    }

    [RelayCommand(CanExecute = nameof(HasCharacter))]
    private void DeleteCharacter()
    {
        if (SelectedCharacter is null) return;
        var index = Characters.IndexOf(SelectedCharacter);
        Characters.RemoveAt(index);
        SelectedCharacter = Characters.Count == 0 ? null : Characters[Math.Min(index, Characters.Count - 1)];
    }
    [ObservableProperty] private string _toggleHotkey;
    [ObservableProperty] private string _nextPartHotkey;
    [ObservableProperty] private bool _autoCheck;
    [ObservableProperty] private double _autoCheckDelaySeconds;
    [ObservableProperty] private string _chunkLimitBytes;
    [ObservableProperty] private bool _numberParts;
    [ObservableProperty] private bool _normalizePunctuation;
    [ObservableProperty] private double _idleOpacity;
    [ObservableProperty] private double _activeOpacity;
    [ObservableProperty] private bool _startWithWindows;

    [ObservableProperty] private string _keyStatus = "";
    [ObservableProperty] private bool _keyFound;
    [ObservableProperty] private string _validationError = "";

    /// <summary>Creates .env from the template if missing, then opens it in Notepad for pasting the key.</summary>
    [RelayCommand]
    private void OpenEnvFile()
    {
        var directory = ApiKeyResolver.SuggestedEnvDirectory(AppContext.BaseDirectory);
        var path = EnvFile.FindUpwards(AppContext.BaseDirectory) ?? Path.Combine(directory, EnvFile.FileName);
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "# Chattyn: paste your Anthropic API key after the \"=\" and save this file.\n" +
                    "# This file is git-ignored. Never commit or share it.\n" +
                    $"{ApiKeyResolver.VariableName}=\n");
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false });
            KeyStatus = $"Opened {path}. Paste the key after the \"=\", save, then click Reload key.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            KeyFound = false;
            KeyStatus = $"Could not create or open {path}: {ex.Message} " +
                        $"Create the file yourself with one line: {ApiKeyResolver.VariableName}=your-key";
        }
    }

    [RelayCommand]
    private void ReloadKey() => UpdateKeyStatus(_reloadApiKey());

    private void UpdateKeyStatus(ApiKeyLookup lookup)
    {
        KeyFound = lookup.Found;
        KeyStatus = lookup switch
        {
            { Found: true } => $"Key {lookup.MaskedKey} found in {lookup.Source}.",
            { Source: not null } => $"{lookup.Source} has no {ApiKeyResolver.VariableName} value yet.",
            _ => $"No .env file found. Click \"Create / open .env\" and paste your key.",
        };
    }

    /// <summary>Validates and writes the values into the settings object. Returns false and sets ValidationError when invalid.</summary>
    public bool TryApply()
    {
        if (!Hotkey.TryParse(ToggleHotkey, out var toggle, out var toggleError))
            return Fail($"Show/hide hotkey: {toggleError}");
        if (!Hotkey.TryParse(NextPartHotkey, out var nextPart, out var nextError))
            return Fail($"Next part hotkey: {nextError}");
        if (toggle.SameCombination(nextPart))
            return Fail("The two hotkeys must be different.");
        if (!int.TryParse(ChunkLimitBytes, out var limit) || limit is < 50 or > 4000)
            return Fail("Chat message limit must be a number between 50 and 4000 (WoW: 255).");
        if (string.IsNullOrWhiteSpace(Model))
            return Fail("Choose a model.");

        _settings.Model = Model.Trim();
        _settings.TranslationLanguages = SplitList(TranslationLanguages, ',', ';', '\n');
        _settings.ExplanationLanguage = ExplanationLanguage.Trim();

        var hadNoCharacters = _settings.Characters.Count == 0;
        _settings.Characters = Characters.Select(c => c.ToProfile()).ToList();
        // The first character someone adds is clearly the one they want to write as.
        if (hadNoCharacters && _settings.Characters.Count > 0)
            _settings.ActiveCharacterId = _settings.Characters[0].Id;
        _settings.LoreWords = SplitList(LoreWords, '\n', ',');
        _settings.ToggleHotkey = toggle.Display;
        _settings.NextPartHotkey = nextPart.Display;
        _settings.AutoCheck = AutoCheck;
        _settings.AutoCheckDelayMs = (int)Math.Round(AutoCheckDelaySeconds * 1000);
        _settings.ChunkLimitBytes = limit;
        _settings.NumberParts = NumberParts;
        _settings.NormalizePunctuation = NormalizePunctuation;
        _settings.IdleOpacity = IdleOpacity;
        _settings.ActiveOpacity = ActiveOpacity;
        _settings.StartWithWindows = StartWithWindows;
        _settings.Normalize();

        ValidationError = "";
        return true;
    }

    private bool Fail(string message)
    {
        ValidationError = message;
        return false;
    }

    private static List<string> SplitList(string text, params char[] separators) =>
        text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
