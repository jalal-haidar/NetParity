using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetParity.Core;

public sealed class AppSettings
{
    /// <summary>Negative means "not yet positioned"; the window centres itself on first run.</summary>
    public double WindowLeft { get; set; } = -1;

    public double WindowTop { get; set; } = -1;

    public double Opacity { get; set; } = 0.94;

    public SpeedUnit UnitMode { get; set; } = SpeedUnit.Auto;

    public string LatencyHost { get; set; } = "1.1.1.1";

    public int LatencyPort { get; set; } = 443;

    public bool ShowLatency { get; set; } = true;

    public bool RunAtStartup { get; set; }

    public string Accent { get; set; } = "#00F2FF";
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetParity",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file must never stop the app from starting.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception)
        {
            // Losing a preference is not worth interrupting the user over.
        }
    }
}
