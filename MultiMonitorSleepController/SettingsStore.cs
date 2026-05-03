using System.Text.Json;

namespace MultiMonitorSleepController;

public sealed class SettingsStore
{
    private readonly string _settingsFilePath;

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolderPath = Path.Combine(appDataPath, "MultiMonitorSleepController");
        _settingsFilePath = Path.Combine(appFolderPath, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            var rawJson = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(rawJson, _serializerOptions);

            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var folder = Path.GetDirectoryName(_settingsFilePath);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var rawJson = JsonSerializer.Serialize(settings, _serializerOptions);
        File.WriteAllText(_settingsFilePath, rawJson);
    }
}
