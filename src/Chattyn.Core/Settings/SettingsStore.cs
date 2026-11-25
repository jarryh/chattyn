using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chattyn.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chattyn");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, "settings.json");

    public string FilePath { get; } = path;

    /// <summary>
    /// Returns the saved settings (upgraded from older versions), or first-run defaults when no file
    /// exists. A corrupt file is kept as settings.json.bak and replaced by defaults.
    /// A file that cannot be read at all (locked, no access) throws, so it is never overwritten.
    /// </summary>
    public (AppSettings Settings, bool IsFirstRun) Load()
    {
        if (!File.Exists(FilePath))
            return (AppSettings.CreateDefault(), true);

        var json = File.ReadAllText(FilePath);
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? throw new JsonException("Empty settings file.");
            settings.Migrate();
            settings.Normalize();
            return (settings, false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            return (AppSettings.CreateDefault(), true);
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
