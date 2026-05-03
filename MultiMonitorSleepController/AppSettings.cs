namespace MultiMonitorSleepController;

public sealed class AppSettings
{
    public List<MonitorProfile> Profiles { get; set; } = new();

    public Dictionary<string, MonitorControlMode> MonitorModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> BrightnessOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DdcBrightnessType> DdcBrightnessTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> CustomMonitorNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DarkMode { get; set; } = true;
}

public sealed class MonitorProfile
{
    public string Name { get; set; } = "New Profile";

    public Dictionary<string, bool> MonitorStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HotkeyBinding? Hotkey { get; set; }

    public override string ToString()
    {
        if (Hotkey is null || !Hotkey.IsAssigned)
        {
            return Name;
        }

        return $"{Name} ({Hotkey})";
    }
}
