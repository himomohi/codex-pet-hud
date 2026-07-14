using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexPetLimitRings");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string AlertStatePath => Path.Combine(DataDirectory, "alert-state.json");

    public OverlaySettings LoadSettings()
    {
        var settings = Read<OverlaySettings>(SettingsPath) ?? new OverlaySettings();
        settings.Normalize();
        return settings;
    }

    public AlertDeliveryState LoadAlertState()
    {
        var state = Read<AlertDeliveryState>(AlertStatePath) ?? new AlertDeliveryState();
        state.Normalize();
        return state;
    }
    public void SaveSettings(OverlaySettings value) { value.Normalize(); Write(SettingsPath, value); }
    public void SaveAlertState(AlertDeliveryState value) => Write(AlertStatePath, value);

    private static T? Read<T>(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch { return default; }
    }

    private void Write<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Settings write failed: {error.Message}");
            try { File.Delete(temporary); } catch { }
        }
    }
}
