using System.Windows.Forms;

namespace MultiMonitorSleepController;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

public sealed class HotkeyBinding
{
    public HotkeyModifiers Modifiers { get; set; }

    public Keys Key { get; set; } = Keys.None;

    public bool IsAssigned => Key != Keys.None;

    public override string ToString()
    {
        if (!IsAssigned)
        {
            return "None";
        }

        var parts = new List<string>();

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
