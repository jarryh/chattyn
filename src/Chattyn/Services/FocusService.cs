using Chattyn.Interop;

namespace Chattyn.Services;

/// <summary>
/// Remembers which window (normally WoW) had focus before Chattyn was activated,
/// and gives focus back to it after copying.
/// </summary>
public sealed class FocusService
{
    private IntPtr _previous;

    /// <summary>Call right before Chattyn takes focus (e.g. on the global hotkey).</summary>
    public void CaptureForeground() => Remember(NativeMethods.GetForegroundWindow());

    /// <summary>Remembers <paramref name="hwnd"/> unless it belongs to Chattyn itself.</summary>
    public void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return;
        _previous = hwnd;
    }

    /// <summary>Brings the remembered window back to the front. Returns false if there is none.</summary>
    public bool RestorePrevious()
    {
        if (_previous == IntPtr.Zero || !NativeMethods.IsWindow(_previous)) return false;
        return NativeMethods.SetForegroundWindow(_previous);
    }

    public static void ForceForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);
}
