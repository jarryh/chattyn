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
    private Mutex? _singleInstance;
    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private ApiKeyLookup _apiKey = new(null, null);
    private ClaudeWritingAssistant? _assistant;
    private MainViewModel _vm = null!;
    private MainWindow _main = null!;
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
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _store = new SettingsStore(SettingsStore.DefaultPath);
        (_settings, var isFirstRun) = _store.Load();
        ReloadApiKey();

        // If the key is missing, look again on each check, so a freshly created .env just works.
        _assistant = new ClaudeWritingAssistant(() => _apiKey.Found ? _apiKey.Key : ReloadApiKey().Key);
        var focus = new FocusService();
        _vm = new MainViewModel(_assistant, () => _settings);
        _main = new MainWindow(_vm, focus, () => _settings);
        _main.SettingsRequested += (_, _) => OpenSettings();
        _main.Show();

        _hotkeys = new HotkeyService(_main.Handle);
        _tray = new TrayIcon(
            onShow: () => _main.ShowAndFocus(captureForeground: false),
            onSettings: OpenSettings,
            onQuit: Quit);

        ApplySettings();

        if (!_apiKey.Found)
            _vm.SetStatus("No API key yet: open Settings and click \"Create / open .env\".", isError: true);
        else
            _vm.SetStatus($"Ready. {_settings.ToggleHotkey} opens Chattyn from the game.");

        if (isFirstRun)
        {
            SaveSettings();
            OpenSettings();
        }
    }

    private ApiKeyLookup ReloadApiKey()
    {
        _apiKey = ApiKeyResolver.Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable);
        return _apiKey;
    }

    /// <summary>Pushes current settings into hotkeys, spell check, opacity and the Run key.</summary>
    private void ApplySettings()
    {
        var problems = new List<string>();

        _hotkeys!.UnregisterAll();
        if (_hotkeys.Register(_settings.ToggleHotkey, _main.ToggleFromHotkey) is { } toggleError) problems.Add(toggleError);
        if (_hotkeys.Register(_settings.NextPartHotkey, () => _vm.CopyNextPart()) is { } nextError) problems.Add(nextError);
        _tray!.SetShowHotkey(_settings.ToggleHotkey);

        try
        {
            SpellCheckDictionaries.WriteUserWords(_settings.LoreWords);
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
        _vm.OnSettingsChanged();

        if (problems.Count > 0)
        {
            _vm.SetStatus(problems[0], isError: true);
            _tray.ShowBalloon("Chattyn", string.Join("\n", problems), isError: true);
        }
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
            SaveSettings();
            ApplySettings();
            if (_apiKey.Found && !_vm.StatusIsError) _vm.SetStatus("Settings saved.");
        });
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (_apiKey.Found && _vm.Status.StartsWith("No API key", StringComparison.Ordinal))
                _vm.SetStatus("API key found. Ready.");
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetStatus("Could not save settings: " + ex.Message, isError: true);
        }
    }

    private void Quit()
    {
        _main.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            _main.StoreBounds();
            SaveSettings();
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _assistant?.Dispose();
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.AppendAllText(Path.Combine(SettingsStore.DefaultDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }

        _vm?.SetStatus("Something went wrong: " + e.Exception.Message + " (details in %APPDATA%\\Chattyn\\error.log)", isError: true);
        e.Handled = true;
    }
}
