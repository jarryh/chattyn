using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chattyn.Core.Assistant;
using Chattyn.Core.Settings;
using Chattyn.Services;
using Chattyn.ViewModels;

namespace Chattyn;

public partial class App : Application
{
    private const string ToggleHotkeyName = "toggle";
    private const string NextPartHotkeyName = "next-part";

    private Mutex? _singleInstance;
    private SettingsStore? _store;
    private AppSettings _settings = AppSettings.CreateDefault();
    private ApiKeyLookup _apiKey = new(null, null);
    private ClaudeWritingAssistant? _assistant;
    private FocusService? _focus;
    private MainViewModel? _vm;
    private MainWindow? _main;
    private TrayIcon? _tray;
    private HotkeyService? _hotkeys;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Chattyn.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Chattyn is already running. Look for its icon in the notification area.", "Chattyn");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // A failure here must end the process: with no window and no tray icon there would be
        // nothing to quit it with, and the mutex would block every later start.
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show(
                "Chattyn could not start:\n\n" + ex.Message + "\n\nDetails are in %APPDATA%\\Chattyn\\error.log.",
                "Chattyn", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void Initialize()
    {
        _store = new SettingsStore(SettingsStore.DefaultPath);
        (_settings, var isFirstRun) = _store.Load();
        ReloadApiKey();

        // Read the key again for every check: editing .env takes effect without a restart.
        _assistant = new ClaudeWritingAssistant(() => ReloadApiKey().Key);
        _focus = new FocusService();
        _vm = new MainViewModel(_assistant, () => _settings);
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _main = new MainWindow(_vm, _focus, () => _settings);
        _main.SettingsRequested += (_, _) => OpenSettings();
        _main.Show();

        _hotkeys = new HotkeyService(_main.Handle);
        _tray = new TrayIcon(
            onShow: () => _main.ShowAndFocus(captureForeground: false),
            onSettings: OpenSettings,
            onQuit: Quit);

        if (ApplySettings())
            ShowIdleStatus($"Ready. {_settings.ToggleHotkey} opens Chattyn from the game.");

        // Always save once: writes first-run defaults, or the upgraded format of an older file.
        SaveSettings();
        if (isFirstRun) OpenSettings();
    }

    private ApiKeyLookup ReloadApiKey()
    {
        _apiKey = ApiKeyResolver.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable);
        return _apiKey;
    }

    /// <summary>The status when nothing is wrong: a reminder about the missing key, or the given message.</summary>
    private void ShowIdleStatus(string message)
    {
        if (_apiKey.Found) _vm!.SetStatus(message);
        else _vm!.SetStatus("No API key yet: open Settings and click \"Create / open .env\".", isError: true);
    }

    /// <summary>
    /// Pushes current settings into hotkeys, spell check, opacity and the Run key.
    /// Returns false, with the first problem in the status line, when something could not be applied.
    /// </summary>
    private bool ApplySettings()
    {
        var problems = new List<string>();

        if (_hotkeys!.Register(ToggleHotkeyName, _settings.ToggleHotkey, _main!.ToggleFromHotkey) is { } toggleError)
            problems.Add(toggleError);
        _tray!.SetShowHotkey(_settings.ToggleHotkey);

        // Re-register with the (possibly new) combination if a long post is half sent.
        _hotkeys.Unregister(NextPartHotkeyName);
        if (UpdateNextPartHotkey() is { } nextError) problems.Add(nextError);

        try
        {
            // Character names count as known words too, so they are not underlined as typos.
            SpellCheckDictionaries.WriteUserWords(_settings.LoreWords.Concat(_settings.Characters.Select(c => c.Name)));
            _main.RefreshSpellCheck();
        }
        catch (IOException ex)
        {
            problems.Add("Could not update the spell-check word list: " + ex.Message);
        }

        try
        {
            StartupRegistration.Apply(_settings.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            problems.Add("Could not change 'Start with Windows': " + ex.Message);
        }

        _main.ApplyOpacity();
        _vm!.OnSettingsChanged();

        if (problems.Count == 0) return true;
        _vm.SetStatus(problems[0], isError: true);
        _tray.ShowBalloon("Chattyn", string.Join("\n", problems), isError: true);
        return false;
    }

    /// <summary>
    /// The "copy next part" hotkey exists only while parts of a long post are waiting. A global
    /// hotkey swallows its key combination in every app, and Ctrl+Alt is how AltGr arrives, so a
    /// permanently registered Ctrl+Alt+letter would block that AltGr character system-wide.
    /// </summary>
    private string? UpdateNextPartHotkey()
    {
        if (_hotkeys is null || _vm is null) return null;

        if (!_vm.HasPendingParts)
        {
            _hotkeys.Unregister(NextPartHotkeyName);
            return null;
        }

        if (_hotkeys.IsRegistered(NextPartHotkeyName)) return null;
        var error = _hotkeys.Register(NextPartHotkeyName, _settings.NextPartHotkey, () => _vm.CopyNextPart());
        return error is null ? null : error + " Use the \"Next part\" button in Chattyn instead.";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCharacter))
        {
            // Remember the chosen character right away, not only on exit.
            if (_hotkeys is not null) SaveSettings();
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.HasPendingParts)) return;

        // Deferred so an error lands after the "Part 1/3 copied" status written by the same operation.
        Dispatcher.BeginInvoke(() =>
        {
            if (UpdateNextPartHotkey() is { } error) _vm?.SetStatus(error, isError: true);
        });
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(_settings, ReloadApiKey(), ReloadApiKey);
        _settingsWindow = new SettingsWindow(vm, onSaved: () =>
        {
            var saved = SaveSettings();
            // ApplySettings replaces any older error with the current state.
            if (ApplySettings() && saved) ShowIdleStatus("Settings saved.");
        });
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (ReloadApiKey().Found && _vm!.Status.StartsWith("No API key", StringComparison.Ordinal))
                _vm.SetStatus("API key found. Ready.");
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private bool SaveSettings()
    {
        try
        {
            _store!.Save(_settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm?.SetStatus("Could not save settings: " + ex.Message, isError: true);
            return false;
        }
    }

    private void Quit()
    {
        if (_main is not null) _main.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            // Only a fully started app has window bounds worth saving; a failed start must not
            // overwrite the settings file with defaults.
            if (_main is not null && _hotkeys is not null)
            {
                _main.StoreBounds();
                SaveSettings();
            }
            _hotkeys?.Dispose();
            _focus?.Dispose();
            _tray?.Dispose();
            _assistant?.Dispose();
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        _vm?.SetStatus("Something went wrong: " + e.Exception.Message + " (details in %APPDATA%\\Chattyn\\error.log)", isError: true);
        e.Handled = true;
    }

    private static void LogError(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.AppendAllText(Path.Combine(SettingsStore.DefaultDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
