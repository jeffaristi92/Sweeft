using System.Text.Json;

namespace DepuradorCarpetas.Core;

/// <summary>Loads and saves the <see cref="AppConfig"/> on disk (JSON).</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Default path: %APPDATA%\Sweeft\config.json</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sweeft",
        "config.json");

    /// <summary>Loads the configuration; returns a new default one if it does not exist or fails.</summary>
    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options)
                       ?? new AppConfig();
        }
        catch
        {
            // Corrupt or inaccessible config: ignored and defaults are used.
        }
        return new AppConfig();
    }

    /// <summary>Saves the configuration, creating the directory if needed.</summary>
    public static void Save(AppConfig config, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }
}
