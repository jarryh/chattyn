using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Chattyn.Core.Assistant;
using Chattyn.Core.Settings;
using Chattyn.Core.Text;
using Chattyn.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chattyn.ViewModels;

/// <summary>An entry of the character picker; <see cref="Id"/> is null for "No character".</summary>
public sealed record CharacterOption(string? Id, string Name);

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly CharacterOption NoCharacter = new(null, "No character");

    private readonly IWritingAssistant _assistant;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _autoCheckTimer;
    private readonly Queue<string> _pendingParts = new();
    private CancellationTokenSource? _checkCts;
    private string? _checkedText;
    private string? _checkedCharacterId;
    private int _totalParts;
    private bool _selectingForThisPostOnly;
    private bool _refreshingCharacters;

    public MainViewModel(IWritingAssistant assistant, Func<AppSettings> settings)
    {
        _assistant = assistant;
        _settings = settings;
        _selected = settings().PreferredChoice;
        _autoCheckTimer = new DispatcherTimer();
        _autoCheckTimer.Tick += (_, _) =>
        {
            _autoCheckTimer.Stop();
            // No "busy" guard: CheckAsync cancels a running check, so newer text always wins.
            if (Input.Trim().Length >= 3 && Input.Trim() != _checkedText) CheckCommand.Execute(null);
        };
        RefreshCharacters();
        UpdateByteInfo();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    [NotifyPropertyChangedFor(nameof(StaleMessage))]
    private string _input = "";

    /// <summary>"No character" followed by the user's characters.</summary>
    public ObservableCollection<CharacterOption> Characters { get; } = [];

    /// <summary>The character the next check is written for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    [NotifyPropertyChangedFor(nameof(StaleMessage))]
    private CharacterOption? _selectedCharacter = NoCharacter; // the picker sets null while its list is rebuilt

    /// <summary>App name plus the active character, so it is visible in the collapsed bar too.</summary>
    public string HeaderTitle => SelectedCharacter?.Id is null ? "Chattyn" : $"Chattyn - {SelectedCharacter.Name}";

    [ObservableProperty] private string _corrected = "";
    [ObservableProperty] private string _natural = "";
    [ObservableProperty] private PostChoice _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    [NotifyPropertyChangedFor(nameof(StaleMessage))]
    private bool _hasResult;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Ready. Ctrl+Enter checks, Enter copies, Esc goes back.";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _byteInfo = "";
    [ObservableProperty] private bool _hasPendingParts;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasTranslations;

    /// <summary>How the last checked post was written; decides the labels of the second version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondLabel))]
    [NotifyPropertyChangedFor(nameof(SecondChipLabel))]
    private InputLanguage _inputLanguage;

    /// <summary>"Corrected" for English posts; a close translation when the post was not in English.</summary>
    public string SecondLabel => InputLanguage == InputLanguage.Native ? "Literal translation" : "Corrected";

    public string SecondChipLabel => InputLanguage == InputLanguage.Native ? "Literal (Alt+2)" : "Corrected (Alt+2)";

    public ObservableCollection<CheckNote> Notes { get; } = [];
    public ObservableCollection<CheckTranslation> Translations { get; } = [];

    /// <summary>The input changed after the last check, so the suggestions belong to older text.</summary>
    private bool IsTextStale => HasResult && Input.Trim() != _checkedText;

    /// <summary>The suggestions were written for a different character than the one now selected.</summary>
    private bool IsCharacterStale => HasResult && SelectedCharacter?.Id != _checkedCharacterId;

    /// <summary>The shown result no longer matches the input or the character; it is dimmed with a hint.</summary>
    public bool IsStale => IsTextStale || IsCharacterStale;

    public string StaleMessage => IsTextStale
        ? "Your text changed after this check. Ctrl+Enter checks again; Enter in the input box now copies your own text."
        : IsCharacterStale
            ? "This result was written for another character. Ctrl+Enter checks again for the one selected now."
            : "";

    /// <summary>True after a copy until the user edits again: reopening then selects the old text for replacing.</summary>
    public bool CopiedSinceLastEdit { get; private set; }

    /// <summary>Raised after a copy so the window can collapse and give focus back to WoW.</summary>
    public event EventHandler? ReturnToGameRequested;

    partial void OnInputChanged(string value)
    {
        CopiedSinceLastEdit = false;
        UpdateByteInfo();
        _autoCheckTimer.Stop();
        if (_settings().AutoCheck && value.Trim().Length > 0)
        {
            _autoCheckTimer.Interval = TimeSpan.FromMilliseconds(_settings().AutoCheckDelayMs);
            _autoCheckTimer.Start();
        }
    }

    partial void OnSelectedChanged(PostChoice value)
    {
        if (!_selectingForThisPostOnly) _settings().PreferredChoice = value;
    }

    partial void OnSelectedCharacterChanged(CharacterOption? value)
    {
        if (_refreshingCharacters || value is null) return;
        _settings().ActiveCharacterId = value.Id;
        SetStatus(value.Id is null
            ? "No character: neutral tone."
            : $"Writing as {value.Name}." + (IsCharacterStale ? " Ctrl+Enter checks again." : ""));
    }

    /// <summary>Alt+C: the next entry of the character picker, wrapping around.</summary>
    public void CycleCharacter()
    {
        if (Characters.Count < 2)
        {
            SetStatus("No characters yet. Add one in Settings.");
            return;
        }
        var index = Characters.IndexOf(SelectedCharacter ?? NoCharacter);
        SelectedCharacter = Characters[(index + 1) % Characters.Count];
    }

    /// <summary>Rebuilds the picker from settings and selects the active character.</summary>
    private void RefreshCharacters()
    {
        var settings = _settings();
        _refreshingCharacters = true;
        try
        {
            Characters.Clear();
            Characters.Add(NoCharacter);
            foreach (var character in settings.Characters)
                Characters.Add(new CharacterOption(character.Id, character.DisplayName));
            SelectedCharacter = Characters.FirstOrDefault(c => c.Id == settings.ActiveCharacterId) ?? NoCharacter;
        }
        finally
        {
            _refreshingCharacters = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CheckAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0)
        {
            SetStatus("Type your post first.");
            return;
        }

        _autoCheckTimer.Stop();
        _checkCts?.Cancel();
        var cts = new CancellationTokenSource();
        _checkCts = cts;

        IsBusy = true;
        SetStatus("Checking...");
        var stopwatch = Stopwatch.StartNew();
        var options = BuildOptions();
        try
        {
            var result = await _assistant.CheckAsync(text, options, cts.Token);
            if (cts.IsCancellationRequested) return;

            _checkedText = text;
            _checkedCharacterId = options.Character?.Id;
            InputLanguage = result.InputLanguage;
            Corrected = result.Corrected;
            Natural = result.Natural;
            Replace(Notes, result.Notes);
            Replace(Translations, result.Translations);
            HasNotes = Notes.Count > 0;
            HasTranslations = Translations.Count > 0;
            HasResult = true;
            OnPropertyChanged(nameof(IsStale));
            OnPropertyChanged(nameof(StaleMessage));

            // "Mine" would paste non-English text into the chat. Switch for this post only;
            // the saved preference stays as the user set it.
            if (result.InputLanguage != InputLanguage.English && Selected == PostChoice.Mine)
            {
                _selectingForThisPostOnly = true;
                Selected = PostChoice.Natural;
                _selectingForThisPostOnly = false;
            }

            SetStatus(DescribeResult(result, stopwatch.Elapsed.TotalSeconds));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Replaced by a newer check.
        }
        catch (WritingAssistantException) when (cts.IsCancellationRequested)
        {
            // Also replaced: a newer check owns the status line now.
        }
        catch (WritingAssistantException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            if (ReferenceEquals(_checkCts, cts))
            {
                _checkCts = null;
                IsBusy = false;
            }
            cts.Dispose();
        }
    }

    /// <summary>
    /// Copies a version of the post (split into chat-sized parts) and asks the window to return to WoW.
    /// <paramref name="source"/> is the result box that has focus, or null for the input box.
    /// </summary>
    public void CopyAndReturn(PostChoice? source)
    {
        // A result for another character is still a valid version of this text, so only a
        // changed text falls back to the writer's own words.
        var choice = source ?? (HasResult && !IsTextStale ? Selected : PostChoice.Mine);
        var text = TextFor(choice);
        if (string.IsNullOrWhiteSpace(text))
        {
            choice = PostChoice.Mine;
            text = Input;
        }

        var parts = PrepareParts(text);
        if (parts.Count == 0)
        {
            SetStatus("Nothing to copy.");
            return;
        }

        _pendingParts.Clear();
        foreach (var part in parts) _pendingParts.Enqueue(part);
        _totalParts = parts.Count;

        if (!CopyNextPart()) return;
        if (choice == PostChoice.Mine && source is null && IsTextStale)
            SetStatus(Status + " (your own text: it changed after the check)");

        CopiedSinceLastEdit = true;
        ReturnToGameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Next part" button in the header.</summary>
    [RelayCommand]
    private void NextPart() => CopyNextPart();

    /// <summary>Copies the next waiting part of a long post. Returns false when nothing was copied.</summary>
    public bool CopyNextPart()
    {
        if (_pendingParts.Count == 0)
        {
            SetStatus("No more parts to copy.");
            HasPendingParts = false;
            return false;
        }

        var part = _pendingParts.Peek();
        if (!ClipboardService.TrySetText(part))
        {
            SetStatus("Could not copy: the clipboard is busy. Try again.", isError: true);
            return false;
        }

        _pendingParts.Dequeue();
        HasPendingParts = _pendingParts.Count > 0;
        var number = _totalParts - _pendingParts.Count;
        SetStatus(_totalParts == 1
            ? "Copied. Paste it in WoW chat with Ctrl+V."
            : HasPendingParts
                ? $"Part {number}/{_totalParts} copied. Next part: {_settings().NextPartHotkey}"
                : $"Part {number}/{_totalParts} copied (last part).");
        return true;
    }

    public void SetStatus(string message, bool isError = false)
    {
        Status = message;
        StatusIsError = isError;
    }

    /// <summary>Call after settings change: characters, byte limit and auto-check may differ.</summary>
    public void OnSettingsChanged()
    {
        RefreshCharacters();
        UpdateByteInfo();
        if (!_settings().AutoCheck) _autoCheckTimer.Stop();
    }

    private string DescribeResult(CheckResult result, double seconds)
    {
        var version = Selected switch
        {
            PostChoice.Natural => "the natural version",
            PostChoice.Corrected => result.InputLanguage == InputLanguage.Native ? "the literal translation" : "the corrected version",
            _ => "your own text",
        };
        var copyHint = $"Enter copies {version}. ({seconds:0.0} s)";
        var count = result.Notes.Count;

        return result.InputLanguage switch
        {
            InputLanguage.Native => $"Translated to English. {copyHint}",
            InputLanguage.Mixed => $"Filled in the English words. {copyHint}",
            _ when count == 0 => $"Looks good. {copyHint}",
            _ => $"{count} suggestion{(count == 1 ? "" : "s")}. {copyHint}",
        };
    }

    private string TextFor(PostChoice choice) => choice switch
    {
        PostChoice.Corrected => Corrected,
        PostChoice.Natural => Natural,
        _ => Input,
    };

    private IReadOnlyList<string> PrepareParts(string text)
    {
        var settings = _settings();
        var prepared = settings.NormalizePunctuation ? AsciiNormalizer.Normalize(text) : text.Trim();
        return ChatChunker.Split(prepared, settings.ChunkLimitBytes, settings.NumberParts);
    }

    private void UpdateByteInfo()
    {
        var settings = _settings();
        var prepared = settings.NormalizePunctuation ? AsciiNormalizer.Normalize(Input) : Input.Trim();
        var bytes = ChatChunker.Bytes(prepared);
        if (bytes <= settings.ChunkLimitBytes)
        {
            ByteInfo = $"{bytes}/{settings.ChunkLimitBytes} bytes";
            return;
        }
        var parts = ChatChunker.Split(prepared, settings.ChunkLimitBytes, settings.NumberParts).Count;
        ByteInfo = $"{bytes} bytes - {parts} chat messages";
    }

    private AssistantOptions BuildOptions()
    {
        var s = _settings();
        return new AssistantOptions(s.Model, s.TranslationLanguages, s.ExplanationLanguage, s.ActiveCharacter, s.LoreWords);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
