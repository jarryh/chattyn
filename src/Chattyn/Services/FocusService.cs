using Chattyn.Interop;

namespace Chattyn.Services;

/// <summary>
/// Knows which other window (normally WoW) was last in front, and gives focus back to it after
/// copying. It listens to the system's "foreground changed" event, because by the time Chattyn is
/// being activated by a mouse click, Windows no longer reports any foreground window.
/// Must be created on the UI thread: the event is delivered through its message loop.
/// </summary>
public sealed class FocusService : IDisposable
{
    // Taskbar and tray surfaces: clicking the tray icon must not make them the "game" to return to.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
    };

    private readonly NativeMethods.WinEventProc _callback; // field keeps the delegate alive for the hook
    private readonly IntPtr _hook;
    private IntPtr _previous;

    public FocusService()
    {
        _callback = OnForegroundChanged;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        CaptureForeground();
    }

    /// <summary>Notes the current foreground window; a cheap extra right before taking focus by hotkey.</summary>
    public void CaptureForeground() => Remember(NativeMethods.GetForegroundWindow());

    /// <summary>Brings the remembered window back to the front. Returns false if there is none.</summary>
    public bool RestorePrevious()
    {
        if (_previous == IntPtr.Zero || !NativeMethods.IsWindow(_previous)) return false;
        return NativeMethods.SetForegroundWindow(_previous);
    }

    public static void ForceForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Remember(hwnd);

    private void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return;
        if (ShellClasses.Contains(NativeMethods.GetClassName(hwnd))) return;
        _previous = hwnd;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hook);
    }
}
