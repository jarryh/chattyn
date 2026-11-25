using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Chattyn.Core.Settings;
using Chattyn.Services;
using Chattyn.ViewModels;

namespace Chattyn;

/// <summary>
/// The overlay: a slim always-on-top bar while you play, expanded into the writing panel
/// by the hotkey. Enter copies and hands focus back to the game.
/// </summary>
public partial class MainWindow : Window
{
    private const double CollapsedHeight = 36;
    private const double MinExpandedHeight = 260;

    private readonly MainViewModel _vm;
    private readonly FocusService _focus;
    private readonly Func<AppSettings> _settings;
    private bool _expanded = true;

    public MainWindow(MainViewModel vm, FocusService focus, Func<AppSettings> settings)
    {
        InitializeComponent();
        _vm = vm;
        _focus = focus;
        _settings = settings;
        DataContext = vm;

        vm.ReturnToGameRequested += (_, _) => ReturnToGame();
        Activated += (_, _) => ApplyOpacity();
        Deactivated += (_, _) => ApplyOpacity();
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true; // Alt+F4 hides to tray; Quit is in the tray menu.
            Hide();
        };

        PlaceWindow();
        SetExpanded(false);
        RefreshSpellCheck();
        ApplyOpacity();
    }

    public event EventHandler? SettingsRequested;

    /// <summary>Set by the app on Quit so Closing is not turned into Hide.</summary>
    public bool AllowClose { get; set; }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Global hotkey: open the writing panel, or go back to the game if it is already open.</summary>
    public void ToggleFromHotkey()
    {
        if (IsActive && _expanded) ReturnToGame();
        else ShowAndFocus();
    }

    public void ShowAndFocus(bool captureForeground = true)
    {
        if (captureForeground) _focus.CaptureForeground();
        if (!IsVisible) Show();
        SetExpanded(true);
        Activate();
        FocusService.ForceForeground(Handle);
        InputBox.Focus();
        Keyboard.Focus(InputBox);

        // After a copy, typing replaces the old post; otherwise continue where you left off.
        if (_vm.CopiedSinceLastEdit) InputBox.SelectAll();
        else InputBox.CaretIndex = InputBox.Text.Length;
    }

    public void ReturnToGame()
    {
        SetExpanded(false);
        _focus.RestorePrevious();
    }

    public void ApplyOpacity() =>
        Opacity = IsActive ? _settings().ActiveOpacity : _settings().IdleOpacity;

    public void RefreshSpellCheck()
    {
        SpellCheckDictionaries.Attach(InputBox);
        SpellCheckDictionaries.Attach(NaturalBox);
        SpellCheckDictionaries.Attach(CorrectedBox);
    }

    /// <summary>Copies the window position and size into settings (saved on exit).</summary>
    public void StoreBounds()
    {
        var s = _settings();
        s.Left = Left;
        s.Top = Top;
        s.Width = ActualWidth > 0 ? ActualWidth : Width;
        if (_expanded && ActualHeight >= MinExpandedHeight) s.ExpandedHeight = ActualHeight;
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        if (_expanded && ActualHeight >= MinExpandedHeight) _settings().ExpandedHeight = ActualHeight;

        _expanded = expanded;
        Body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ResizeMode = expanded ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        MinHeight = expanded ? MinExpandedHeight : CollapsedHeight;
        Height = expanded ? Math.Max(_settings().ExpandedHeight, MinExpandedHeight) : CollapsedHeight;
        ExpandButton.Content = expanded ? "" : ""; // chevron up / down
    }

    private void PlaceWindow()
    {
        var s = _settings();
        Width = s.Width;
        var area = SystemParameters.WorkArea;
        var left = s.Left ?? area.Right - Width - 40;
        var top = s.Top ?? area.Top + 40;

        // A saved position on a monitor that is no longer connected: fall back to the default.
        var onScreen =
            left >= SystemParameters.VirtualScreenLeft - 20 &&
            top >= SystemParameters.VirtualScreenTop - 20 &&
            left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        Left = onScreen ? left : area.Right - Width - 40;
        Top = onScreen ? top : area.Top + 40;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // While the character list is open, Enter and Esc belong to the list.
        if (CharacterBox.IsDropDownOpen) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        switch (key)
        {
            case Key.C when modifiers == ModifierKeys.Alt:
                _vm.CycleCharacter();
                e.Handled = true;
                break;

            case Key.Escape when modifiers == ModifierKeys.None:
                ReturnToGame();
                e.Handled = true;
                break;

            case Key.Enter when modifiers == ModifierKeys.Control:
                _vm.CheckCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter when modifiers == ModifierKeys.None:
                PostChoice? source = Keyboard.FocusedElement == NaturalBox ? PostChoice.Natural
                    : Keyboard.FocusedElement == CorrectedBox ? PostChoice.Corrected
                    : null;
                _vm.CopyAndReturn(source);
                e.Handled = true;
                break;

            case Key.D1 or Key.NumPad1 when modifiers == ModifierKeys.Alt:
                _vm.Selected = PostChoice.Natural;
                e.Handled = true;
                break;

            case Key.D2 or Key.NumPad2 when modifiers == ModifierKeys.Alt:
                _vm.Selected = PostChoice.Corrected;
                e.Handled = true;
                break;

            case Key.D3 or Key.NumPad3 when modifiers == ModifierKeys.Alt:
                _vm.Selected = PostChoice.Mine;
                e.Handled = true;
                break;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleExpand_Click(sender, e);
            return;
        }
        DragMove();
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (_expanded) ReturnToGame();
        else ShowAndFocus(captureForeground: false); // FocusService already knows the game window
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // After picking a character, typing should continue in the post.
    private void CharacterBox_DropDownClosed(object? sender, EventArgs e)
    {
        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _focus.RestorePrevious();
    }
}
