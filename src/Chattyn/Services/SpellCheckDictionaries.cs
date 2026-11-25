using System.IO;
using System.Windows.Controls;
using Chattyn.Core.Settings;

namespace Chattyn.Services;

/// <summary>
/// Feeds WPF's spell checker with the built-in WoW term list plus the user's own lore words,
/// so names like "Sin'dorei" are not underlined.
/// </summary>
public static class SpellCheckDictionaries
{
    private static readonly Uri BuiltIn = new("pack://application:,,,/Assets/wow-terms.lex");
    private static readonly string UserLexPath = Path.Combine(SettingsStore.DefaultDirectory, "user-terms.lex");

    /// <summary>Writes the user's words to %APPDATA%\Chattyn\user-terms.lex.</summary>
    public static void WriteUserWords(IEnumerable<string> words)
    {
        Directory.CreateDirectory(SettingsStore.DefaultDirectory);
        // Split multi-word entries ("Bal'a dash") into single words; the speller checks word by word.
        var tokens = words
            .SelectMany(w => w.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        File.WriteAllLines(UserLexPath, tokens);
    }

    /// <summary>(Re)attaches both dictionaries; call again after the user words change.</summary>
    public static void Attach(TextBox textBox)
    {
        var dictionaries = SpellCheck.GetCustomDictionaries(textBox);
        dictionaries.Clear();
        dictionaries.Add(BuiltIn);
        if (File.Exists(UserLexPath)) dictionaries.Add(new Uri(UserLexPath));
    }
}
