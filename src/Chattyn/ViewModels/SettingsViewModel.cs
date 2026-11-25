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
        _characterNotes = settings.CharacterNotes;
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
    [ObservableProperty] private string _characterNotes;
    [ObservableProperty] private string _loreWords;
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
        if (!Hotkey.TryParse(ToggleHotkey, out _, out var toggleError))
            return Fail($"Show/hide hotkey: {toggleError}");
        if (!Hotkey.TryParse(NextPartHotkey, out _, out var nextError))
            return Fail($"Next part hotkey: {nextError}");
        if (string.Equals(ToggleHotkey.Replace(" ", ""), NextPartHotkey.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            return Fail("The two hotkeys must be different.");
        if (!int.TryParse(ChunkLimitBytes, out var limit) || limit is < 50 or > 4000)
            return Fail("Chat message limit must be a number between 50 and 4000 (WoW: 255).");
        if (string.IsNullOrWhiteSpace(Model))
            return Fail("Choose a model.");

        _settings.Model = Model.Trim();
        _settings.TranslationLanguages = SplitList(TranslationLanguages, ',', ';', '\n');
        _settings.ExplanationLanguage = ExplanationLanguage.Trim();
        _settings.CharacterNotes = CharacterNotes.Trim();
        _settings.LoreWords = SplitList(LoreWords, '\n', ',');
        _settings.ToggleHotkey = ToggleHotkey.Trim();
        _settings.NextPartHotkey = NextPartHotkey.Trim();
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
