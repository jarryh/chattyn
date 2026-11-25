using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Chattyn.Interop;

namespace Chattyn.Services;

/// <summary>A global hotkey such as "Ctrl+Alt+Space", parsed into Win32 modifier flags and a virtual key.</summary>
public sealed record Hotkey(uint Modifiers, uint VirtualKey, string Display)
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey, out string? error)
    {
        hotkey = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty.";
            return false;
        }

        uint modifiers = 0;
        Key? key = null;
        var display = new List<string>();
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    display.Add("Ctrl");
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    display.Add("Alt");
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    display.Add("Shift");
                    break;
                case "win" or "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    display.Add("Win");
                    break;
                default:
                    if (key is not null)
                    {
                        error = $"'{text}' has more than one key. Use modifiers plus one key, e.g. Ctrl+Alt+Space.";
                        return false;
                    }
                    if (!TryParseKey(raw, out var parsed))
                    {
                        error = $"Unknown key '{raw}'.";
                        return false;
                    }
                    key = parsed;
                    // Key has aliases (PageDown == Next), so show the name the user typed, in enum casing.
                    display.Add(raw.Length == 1
                        ? raw.ToUpperInvariant()
                        : Enum.GetNames<Key>().FirstOrDefault(n => n.Equals(raw, StringComparison.OrdinalIgnoreCase)) ?? raw);
                    break;
            }
        }

        if (key is null)
        {
            error = $"'{text}' has no key, only modifiers.";
            return false;
        }
        if ((modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN)) == 0)
        {
            error = "Use Ctrl, Alt or Win in a global hotkey, so normal typing in WoW is not affected.";
            return false;
        }

        hotkey = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key.Value), string.Join("+", display));
        return true;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        if (text.Length == 1 && char.IsDigit(text[0]))
            return Enum.TryParse("D" + text, out key);
        // Enum.TryParse would also accept raw numbers such as "65"; only key names are allowed.
        if (text.All(char.IsDigit)) return false;
        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None;
    }

    /// <summary>True when both describe the same key combination, however they were typed.</summary>
    public bool SameCombination(Hotkey other) =>
        Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    public override string ToString() => Display;
}
