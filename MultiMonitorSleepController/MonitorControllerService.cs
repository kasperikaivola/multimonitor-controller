using System.Drawing;
using System.Runtime.InteropServices;

namespace MultiMonitorSleepController;

public sealed class MonitorControllerService : IDisposable
{
    private readonly Dictionary<string, NativeMonitorBinding> _nativeBindings = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MonitorDescriptor> RefreshMonitors()
    {
        ReleasePhysicalMonitorHandles();

        var discoveredBindings = new List<NativeMonitorBinding>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        foreach (var binding in discoveredBindings)
        {
            _nativeBindings[binding.Descriptor.Id] = binding;
        }

        return _nativeBindings.Values
            .Select(binding => binding.Descriptor)
            .OrderBy(descriptor => descriptor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool callback(IntPtr hMonitor, IntPtr _, ref NativeMethods.Rect __, IntPtr ___)
        {
            var monitorInfo = new NativeMethods.MonitorInfoEx
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                szDevice = string.Empty
            };

            if (!NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                return true;
            }

            var bounds = Rectangle.FromLTRB(
                monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right,
                monitorInfo.rcMonitor.Bottom);

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var physicalMonitorCount) ||
                physicalMonitorCount == 0)
            {
                discoveredBindings.Add(CreateLogicalFallbackBinding(monitorInfo, bounds));
                return true;
            }

            var physicalMonitors = new NativeMethods.PhysicalMonitor[physicalMonitorCount];

            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, physicalMonitorCount, physicalMonitors))
            {
                discoveredBindings.Add(CreateLogicalFallbackBinding(monitorInfo, bounds));
                return true;
            }

            for (var index = 0; index < physicalMonitors.Length; index++)
            {
                var physical = physicalMonitors[index];
                var id = $"{monitorInfo.szDevice}#{index}";
                uint vcpType;
                uint currentPowerMode;
                uint maxValue;

                var supportsPowerControl = NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                    physical.hPhysicalMonitor,
                    NativeMethods.VcpCodePowerMode,
                    out vcpType,
                    out currentPowerMode,
                    out maxValue);

                bool? isOn = null;

                if (supportsPowerControl)
                {
                    isOn = !IsPowerOffValue(currentPowerMode);
                }

                var descriptor = new MonitorDescriptor
                {
                    Id = id,
                    DeviceName = monitorInfo.szDevice,
                    FriendlyName = physical.szPhysicalMonitorDescription,
                    Bounds = bounds,
                    SupportsPowerControl = supportsPowerControl,
                    IsOn = isOn
                };

                discoveredBindings.Add(new NativeMonitorBinding
                {
                    Descriptor = descriptor,
                    Handle = physical.hPhysicalMonitor
                });
            }

            return true;

            static NativeMonitorBinding CreateLogicalFallbackBinding(NativeMethods.MonitorInfoEx monitorInfo, Rectangle bounds)
            {
                var descriptor = new MonitorDescriptor
                {
                    Id = $"{monitorInfo.szDevice}#logical",
                    DeviceName = monitorInfo.szDevice,
                    FriendlyName = "Logical display (no DDC/CI)",
                    Bounds = bounds,
                    SupportsPowerControl = false,
                    IsOn = true
                };

                return new NativeMonitorBinding
                {
                    Descriptor = descriptor,
                    Handle = IntPtr.Zero
                };
            }
        }
    }

    public IReadOnlyList<MonitorOperationResult> ApplyPowerStates(IReadOnlyDictionary<string, bool> desiredStates)
    {
        var results = new List<MonitorOperationResult>();

        foreach (var state in desiredStates)
        {
            results.Add(SetPowerState(state.Key, state.Value));
        }

        return results;
    }

    public uint? GetInitialBrightness(IReadOnlyDictionary<string, int> offsets, IReadOnlyDictionary<string, DdcBrightnessType> types)
    {
        foreach (var binding in _nativeBindings.Values)
        {
            if (binding.Handle != IntPtr.Zero && binding.Descriptor.SupportsPowerControl)
            {
                var vcpCode = types != null && types.TryGetValue(binding.Descriptor.Id, out var type)
                    ? (byte)type
                    : NativeMethods.VcpCodeBrightness;

                if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                        binding.Handle,
                        vcpCode,
                        out _,
                        out var currentValue,
                        out _))
                {
                    int offset = offsets != null && offsets.TryGetValue(binding.Descriptor.Id, out var o) ? o : 0;
                    return (uint)Math.Clamp((int)currentValue - offset, 0, 100);
                }
            }
        }

        return null;
    }

    public void SetGlobalBrightness(uint baseBrightness, IReadOnlyDictionary<string, int> offsets, IReadOnlyDictionary<string, DdcBrightnessType> types)
    {
        foreach (var binding in _nativeBindings.Values)
        {
            if (binding.Handle != IntPtr.Zero && binding.Descriptor.SupportsPowerControl)
            {
                int offset = offsets != null && offsets.TryGetValue(binding.Descriptor.Id, out var o) ? o : 0;
                int target = Math.Clamp((int)baseBrightness + offset, 0, 100);
                
                var vcpCode = types != null && types.TryGetValue(binding.Descriptor.Id, out var type) 
                    ? (byte)type 
                    : NativeMethods.VcpCodeBrightness;

                NativeMethods.SetVCPFeature(binding.Handle, vcpCode, (uint)target);
            }
        }
    }

    public MonitorOperationResult SetPowerState(string monitorId, bool targetOn)
    {
        if (!_nativeBindings.TryGetValue(monitorId, out var binding))
        {
            return new MonitorOperationResult
            {
                MonitorId = monitorId,
                MonitorName = monitorId,
                TargetOn = targetOn,
                Success = false,
                ErrorMessage = "Monitor was not found. Click Refresh and try again."
            };
        }

        if (!binding.Descriptor.SupportsPowerControl)
        {
            return new MonitorOperationResult
            {
                MonitorId = binding.Descriptor.Id,
                MonitorName = binding.Descriptor.FriendlyName,
                TargetOn = targetOn,
                Success = false,
                ErrorMessage = "This display does not expose DDC/CI power control."
            };
        }

        if (binding.Handle == IntPtr.Zero)
        {
            return new MonitorOperationResult
            {
                MonitorId = binding.Descriptor.Id,
                MonitorName = binding.Descriptor.FriendlyName,
                TargetOn = targetOn,
                Success = false,
                ErrorMessage = "No physical monitor handle is available for this display."
            };
        }

        var targetValue = targetOn ? NativeMethods.VcpPowerOn : NativeMethods.VcpPowerOff;
        var success = NativeMethods.SetVCPFeature(binding.Handle, NativeMethods.VcpCodePowerMode, targetValue);

        if (!success)
        {
            var win32Error = Marshal.GetLastWin32Error();

            // Handles can become stale. Refresh once and retry.
            RefreshMonitors();

            if (_nativeBindings.TryGetValue(monitorId, out var refreshedBinding))
            {
                success = NativeMethods.SetVCPFeature(refreshedBinding.Handle, NativeMethods.VcpCodePowerMode, targetValue);
            }

            if (!success)
            {
                return new MonitorOperationResult
                {
                    MonitorId = binding.Descriptor.Id,
                    MonitorName = binding.Descriptor.FriendlyName,
                    TargetOn = targetOn,
                    Success = false,
                    ErrorMessage = $"SetVCPFeature failed (Win32: {win32Error})."
                };
            }
        }

        return new MonitorOperationResult
        {
            MonitorId = binding.Descriptor.Id,
            MonitorName = binding.Descriptor.FriendlyName,
            TargetOn = targetOn,
            Success = true,
            ErrorMessage = string.Empty
        };
    }

    public void Dispose()
    {
        ReleasePhysicalMonitorHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleasePhysicalMonitorHandles()
    {
        foreach (var binding in _nativeBindings.Values)
        {
            if (binding.Handle != IntPtr.Zero)
            {
                NativeMethods.DestroyPhysicalMonitor(binding.Handle);
            }
        }

        _nativeBindings.Clear();
    }

    private static bool IsPowerOffValue(uint powerValue)
    {
        return powerValue == 0x04 || powerValue == 0x05;
    }

    private sealed class NativeMonitorBinding
    {
        public required MonitorDescriptor Descriptor { get; init; }

        public required IntPtr Handle { get; init; }
    }
}

public sealed class MonitorDescriptor
{
    public required string Id { get; init; }

    public required string DeviceName { get; init; }

    public required string FriendlyName { get; init; }

    public required Rectangle Bounds { get; init; }

    public required bool SupportsPowerControl { get; init; }

    public bool? IsOn { get; init; }
}

public sealed class MonitorOperationResult
{
    public required string MonitorId { get; init; }

    public required string MonitorName { get; init; }

    public required bool TargetOn { get; init; }

    public required bool Success { get; init; }

    public required string ErrorMessage { get; init; }
}
