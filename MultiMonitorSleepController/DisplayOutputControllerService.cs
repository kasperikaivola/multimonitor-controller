using System.Runtime.InteropServices;

namespace MultiMonitorSleepController;

public sealed class DisplayOutputControllerService
{
    private readonly Dictionary<string, DisplaySettingsNative.DevMode> _savedModes = new(StringComparer.OrdinalIgnoreCase);

    public DisplayOutputOperationResult SetOutputEnabled(string deviceName, bool enableOutput)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return DisplayOutputOperationResult.Fail("Display output control failed: missing device name.");
        }

        return enableOutput
            ? EnableOutput(deviceName)
            : DisableOutput(deviceName);
    }

    private DisplayOutputOperationResult DisableOutput(string deviceName)
    {
        if (!TryGetCurrentMode(deviceName, out var currentMode))
        {
            if (_savedModes.ContainsKey(deviceName))
            {
                return DisplayOutputOperationResult.Ok("Display output already disabled.");
            }

            return DisplayOutputOperationResult.Fail("Could not read current display mode for output disable.");
        }

        if (currentMode.dmPelsWidth == 0 || currentMode.dmPelsHeight == 0)
        {
            _savedModes.TryAdd(deviceName, currentMode);
            return DisplayOutputOperationResult.Ok("Display output already disabled.");
        }

        _savedModes[deviceName] = currentMode;

        var disableMode = currentMode;
        disableMode.dmFields = DisplaySettingsNative.DmPosition |
                               DisplaySettingsNative.DmPelsWidth |
                               DisplaySettingsNative.DmPelsHeight;
        disableMode.dmPelsWidth = 0;
        disableMode.dmPelsHeight = 0;

        var updateResult = DisplaySettingsNative.ChangeDisplaySettingsEx(
            deviceName,
            ref disableMode,
            IntPtr.Zero,
            DisplaySettingsNative.CdsUpdateRegistry | DisplaySettingsNative.CdsNoReset,
            IntPtr.Zero);

        if (updateResult != DisplaySettingsNative.DispChangeSuccessful)
        {
            return DisplayOutputOperationResult.Fail($"Output disable request failed with code {updateResult}.");
        }

        var applyResult = DisplaySettingsNative.ChangeDisplaySettingsEx(
            null,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            IntPtr.Zero);

        if (applyResult != DisplaySettingsNative.DispChangeSuccessful)
        {
            return DisplayOutputOperationResult.Fail($"Output disable apply failed with code {applyResult}.");
        }

        return DisplayOutputOperationResult.Ok("Display output disabled at OS level.");
    }

    private DisplayOutputOperationResult EnableOutput(string deviceName)
    {
        if (!_savedModes.TryGetValue(deviceName, out var modeToRestore))
        {
            if (!TryGetRegistryMode(deviceName, out modeToRestore))
            {
                return DisplayOutputOperationResult.Fail("No saved display mode is available to restore output.");
            }
        }

        var updateResult = DisplaySettingsNative.ChangeDisplaySettingsEx(
            deviceName,
            ref modeToRestore,
            IntPtr.Zero,
            DisplaySettingsNative.CdsUpdateRegistry | DisplaySettingsNative.CdsNoReset,
            IntPtr.Zero);

        if (updateResult != DisplaySettingsNative.DispChangeSuccessful)
        {
            return DisplayOutputOperationResult.Fail($"Output enable request failed with code {updateResult}.");
        }

        var applyResult = DisplaySettingsNative.ChangeDisplaySettingsEx(
            null,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            IntPtr.Zero);

        if (applyResult != DisplaySettingsNative.DispChangeSuccessful)
        {
            return DisplayOutputOperationResult.Fail($"Output enable apply failed with code {applyResult}.");
        }

        _savedModes[deviceName] = modeToRestore;
        return DisplayOutputOperationResult.Ok("Display output enabled.");
    }

    private static bool TryGetCurrentMode(string deviceName, out DisplaySettingsNative.DevMode mode)
    {
        mode = DisplaySettingsNative.CreateDevMode();

        return DisplaySettingsNative.EnumDisplaySettingsEx(
            deviceName,
            DisplaySettingsNative.EnumCurrentSettings,
            ref mode,
            0);
    }

    private static bool TryGetRegistryMode(string deviceName, out DisplaySettingsNative.DevMode mode)
    {
        mode = DisplaySettingsNative.CreateDevMode();

        return DisplaySettingsNative.EnumDisplaySettingsEx(
            deviceName,
            DisplaySettingsNative.EnumRegistrySettings,
            ref mode,
            0);
    }
}

public sealed class DisplayOutputOperationResult
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public static DisplayOutputOperationResult Ok(string message)
    {
        return new DisplayOutputOperationResult
        {
            Success = true,
            Message = message
        };
    }

    public static DisplayOutputOperationResult Fail(string message)
    {
        return new DisplayOutputOperationResult
        {
            Success = false,
            Message = message
        };
    }
}

internal static class DisplaySettingsNative
{
    public const int EnumCurrentSettings = -1;

    public const int EnumRegistrySettings = -2;

    public const int DispChangeSuccessful = 0;

    public const int DmPosition = 0x00000020;

    public const int DmPelsWidth = 0x00080000;

    public const int DmPelsHeight = 0x00100000;

    public const uint CdsUpdateRegistry = 0x00000001;

    public const uint CdsNoReset = 0x10000000;

    private const int CchDeviceName = 32;

    private const int CchFormName = 32;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string lpszDeviceName,
        ref DevMode lpDevMode,
        IntPtr hwnd,
        uint dwFlags,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        uint dwFlags,
        IntPtr lParam);

    public static DevMode CreateDevMode()
    {
        return new DevMode
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (short)Marshal.SizeOf<DevMode>()
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public PointL dmPosition;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointL
    {
        public int x;
        public int y;
    }
}
