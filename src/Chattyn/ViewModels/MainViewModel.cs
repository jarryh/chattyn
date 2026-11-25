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

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IWritingAssistant _assistant;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _autoCheckTimer;
    private readonly Queue<string> _pendingParts = new();
    private CancellationTokenSource? _checkCts;
    private string? _checkedText;
    private int _totalParts;

    public MainViewModel(IWritingAssistant assistant, Func<AppSettings> settings)
    {
        _assistant = assistant;
        _settings = settings;
        _selected = settings().PreferredChoice;
        _autoCheckTimer = new DispatcherTimer();
        _autoCheckTimer.Tick += (_, _) =>
        {
            _autoCheckTimer.Stop();
            if (!IsBusy && Input.Trim().Length >= 3 && Input.Trim() != _checkedText) CheckCommand.Execute(null);
        };
        UpdateByteInfo();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    private string _input = "";

    [ObservableProperty] private string _corrected = "";
    [ObservableProperty] private string _natural = "";
    [ObservableProperty] private PostChoice _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    private bool _hasResult;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Ready. Ctrl+Enter checks, Enter copies, Esc goes back.";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _byteInfo = "";
    [ObservableProperty] private bool _hasPendingParts;
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasTranslations;

    public ObservableCollection<CheckNote> Notes { get; } = [];
    public ObservableCollection<CheckTranslation> Translations { get; } = [];

    /// <summary>The input changed after the last check, so the suggestions belong to older text.</summary>
    public bool IsStale => HasResult && Input.Trim() != _checkedText;

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

    partial void OnSelectedChanged(PostChoice value) => _settings().PreferredChoice = value;

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
        try
        {
            var result = await _assistant.CheckAsync(text, BuildOptions(), cts.Token);
            if (cts.IsCancellationRequested) return;

            _checkedText = text;
            Corrected = result.Corrected;
            Natural = result.Natural;
            Replace(Notes, result.Notes);
            Replace(Translations, result.Translations);
            HasNotes = Notes.Count > 0;
            HasTranslations = Translations.Count > 0;
            HasResult = true;
            OnPropertyChanged(nameof(IsStale));

            var seconds = stopwatch.Elapsed.TotalSeconds;
            SetStatus(result.Notes.Count == 0
                ? $"Looks good. ({seconds:0.0} s)"
                : $"{result.Notes.Count} suggestion{(result.Notes.Count == 1 ? "" : "s")}. Enter copies the {Selected.ToString().ToLowerInvariant()} version. ({seconds:0.0} s)");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Replaced by a newer check.
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
        var choice = source ?? (HasResult && !IsStale ? Selected : PostChoice.Mine);
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
        if (choice == PostChoice.Mine && source is null && HasResult && IsStale)
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
                ? $"Part {number}/{_totalParts} copied. After sending it, press {_settings().NextPartHotkey} for the next part."
                : $"Part {number}/{_totalParts} copied (last part).");
        return true;
    }

    public void SetStatus(string message, bool isError = false)
    {
        Status = message;
        StatusIsError = isError;
    }

    /// <summary>Call after settings change: byte limit and auto-check may differ.</summary>
    public void OnSettingsChanged()
    {
        UpdateByteInfo();
        if (!_settings().AutoCheck) _autoCheckTimer.Stop();
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
        return new AssistantOptions(s.Model, s.TranslationLanguages, s.ExplanationLanguage, s.CharacterNotes, s.LoreWords);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
