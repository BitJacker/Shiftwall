using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiftwall.Presentation.Services;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences. Stored locally (%APPDATA%\Shiftwall\settings.json); never contains configuration data.</summary>
public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    public string Language { get; set; } = "it-IT";

    /// <summary>The optional LAN download feature is disabled by default.</summary>
    public bool LanFeatureEnabled { get; set; }

    /// <summary>
    /// Certificate fingerprints (SHA-256) the user confirmed for self-signed FortiGate certificates, keyed by
    /// "address:port". Not secret; no token or password is ever stored.
    /// </summary>
    public Dictionary<string, string> LanConfirmedCertificates { get; set; } = [];
}

public sealed class SettingsStore(string path)
{
    private static readonly SettingsJson Json = new(new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter<ThemeChoice>() } });

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shiftwall", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), Json.AppSettings) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings(); // corrupted or unreadable settings: fall back to defaults
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Json.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are not critical.
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
