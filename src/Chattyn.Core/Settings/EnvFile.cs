namespace Chattyn.Core.Settings;

/// <summary>Minimal .env reader: KEY=VALUE lines, # comments, optional quotes and "export ".</summary>
public static class EnvFile
{
    public const string FileName = ".env";

    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in content.TrimStart('﻿').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line["export ".Length..].TrimStart();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            result[key] = ParseValue(line[(eq + 1)..].Trim());
        }
        return result;
    }

    private static string ParseValue(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\''))
        {
            var close = value.IndexOf(value[0], 1);
            if (close > 0) return value[1..close];
        }

        // Unquoted: a " #" starts an inline comment.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }

    /// <summary>Looks for <paramref name="fileName"/> in <paramref name="startDirectory"/> and each parent folder.</summary>
    public static string? FindUpwards(string startDirectory, string fileName = FileName)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>Where the API key was found. <see cref="Key"/> is null when none was found.</summary>
public sealed record ApiKeyLookup(string? Key, string? Source)
{
    public bool Found => Key is not null;

    /// <summary>Last 4 characters only, for showing in the UI without exposing the key.</summary>
    public string? MaskedKey => Key is { Length: > 8 } k ? "..." + k[^4..] : null;

    // The record's generated ToString would print the key; keep it out of logs and debugger views.
    public override string ToString() => $"ApiKeyLookup {{ Found = {Found}, Source = {Source} }}";
}

public static class ApiKeyResolver
{
    public const string VariableName = "ANTHROPIC_API_KEY";

    /// <summary>
    /// The ANTHROPIC_API_KEY environment variable wins; otherwise the nearest .env file
    /// from <paramref name="startDirectory"/> upward (repo root in development, exe folder when published).
    /// </summary>
    public static ApiKeyLookup Resolve(string startDirectory, Func<string, string?> getEnvironmentVariable)
    {
        var fromEnvironment = getEnvironmentVariable(VariableName)?.Trim();
        if (!string.IsNullOrEmpty(fromEnvironment))
            return new ApiKeyLookup(fromEnvironment, $"environment variable {VariableName}");

        var path = EnvFile.FindUpwards(startDirectory);
        if (path is null) return new ApiKeyLookup(null, null);

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return new ApiKeyLookup(null, path);
        }
        catch (UnauthorizedAccessException)
        {
            return new ApiKeyLookup(null, path);
        }

        return EnvFile.Parse(content).TryGetValue(VariableName, out var key) && key.Length > 0
            ? new ApiKeyLookup(key, path)
            : new ApiKeyLookup(null, path);
    }

    /// <summary>
    /// Folder where a new .env should be created: next to .env.example when running from the
    /// repository, otherwise next to the exe.
    /// </summary>
    public static string SuggestedEnvDirectory(string startDirectory)
    {
        var example = EnvFile.FindUpwards(startDirectory, ".env.example");
        return example is not null ? Path.GetDirectoryName(example)! : startDirectory;
    }
}
