using System.Runtime.InteropServices;
using System.Windows.Interop;
using Chattyn.Interop;

namespace Chattyn.Services;

/// <summary>Registers system-wide hotkeys on a window handle and dispatches WM_HOTKEY to callbacks.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 0xB000;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Window has no HwndSource.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns an error message when it is invalid or taken by another app.</summary>
    public string? Register(string? text, Action handler)
    {
        if (!Hotkey.TryParse(text, out var hotkey, out var error)) return error;

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, hotkey!.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey))
        {
            var code = Marshal.GetLastPInvokeError();
            return code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{hotkey} is already used by another app. Pick another hotkey in Settings."
                : $"Could not register {hotkey} (error {code}).";
        }

        _handlers[id] = handler;
        return null;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) NativeMethods.UnregisterHotKey(_hwnd, id);
        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
