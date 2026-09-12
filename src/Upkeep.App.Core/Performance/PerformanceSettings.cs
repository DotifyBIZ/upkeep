using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Performance;

/// <summary>One power plan Windows offers, and whether it is the active one.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>The performance-related settings Upkeep can read and change for the current user.</summary>
public sealed record PerformanceState(bool AnimationsEnabled, bool TransparencyEnabled, IReadOnlyList<PowerPlan> PowerPlans);

/// <summary>
/// Reads and writes the handful of Windows settings on the Performance tab.
/// <para>
/// Deliberately a handful: animations, transparency and the power plan are settings with a single
/// obvious meaning, which Windows itself exposes and which can be put back exactly as they were.
/// The full "Adjust for best performance" surface is a Windows dialog, and the page links to it
/// rather than reimplementing a control panel from 2001.
/// </para>
/// </summary>
public interface IPerformanceSettings
{
    PerformanceState Read();

    bool SetAnimationsEnabled(bool enabled, out string? failure);

    bool SetTransparencyEnabled(bool enabled, out string? failure);

    bool SetActivePowerPlan(Guid planId, out string? failure);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin SystemParametersInfo/powrprof/registry wrapper; the view model logic built on it is tested against a substitute.")]
public sealed partial class PerformanceSettings : IPerformanceSettings
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string TransparencyValue = "EnableTransparency";

    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private readonly IRegistryProbe _registry;

    public PerformanceSettings(IRegistryProbe registry) => _registry = registry;

    public PerformanceState Read() => new(ReadAnimations(), ReadTransparency(), ReadPowerPlans());

    public bool SetAnimationsEnabled(bool enabled, out string? failure)
    {
        // SPI_SETCLIENTAREAANIMATION is what Settings' own "Animation effects" toggle drives;
        // writing the registry directly would leave running apps on the old value until sign-out.
        if (SystemParametersInfo(SpiSetClientAreaAnimation, 0, enabled ? 1 : 0, SpifUpdateIniFile | SpifSendChange))
        {
            failure = null;
            return true;
        }

        failure = $"SystemParametersInfo failed ({Marshal.GetLastWin32Error()}).";
        return false;
    }

    public bool SetTransparencyEnabled(bool enabled, out string? failure) =>
        _registry.SetCurrentUserBinaryValue(PersonalizeKey, TransparencyValue, BitConverter.GetBytes(enabled ? 1 : 0), out failure);

    public bool SetActivePowerPlan(Guid planId, out string? failure)
    {
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref planId);
        if (result == 0)
        {
            failure = null;
            return true;
        }

        failure = $"PowerSetActiveScheme failed ({result}).";
        return false;
    }

    private static bool ReadAnimations()
    {
        int enabled = 0;
        return SystemParametersInfoRef(SpiGetClientAreaAnimation, 0, ref enabled, 0) && enabled != 0;
    }

    private bool ReadTransparency()
    {
        // Absent means on: that is Windows' default, and the toggle should reflect what the user
        // actually sees rather than what the registry happens to record.
        byte[]? value = _registry.GetBinaryValue(RegistryHiveName.CurrentUser, PersonalizeKey, TransparencyValue);
        return value is null || value.Length < 4 || BitConverter.ToInt32(value, 0) != 0;
    }

    /// <summary>
    /// The plans Windows offers. On modern-standby machines this is often just Balanced, which is
    /// why the page says so rather than pretending the choice exists.
    /// </summary>
    private static List<PowerPlan> ReadPowerPlans()
    {
        var plans = new List<PowerPlan>();

        Guid active = Guid.Empty;
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePointer) == 0 && activePointer != IntPtr.Zero)
        {
            try
            {
                active = Marshal.PtrToStructure<Guid>(activePointer);
            }
            finally
            {
                LocalFree(activePointer);
            }
        }

        uint index = 0;
        while (true)
        {
            uint bufferSize = (uint)Marshal.SizeOf<Guid>();
            byte[] buffer = new byte[bufferSize];

            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /* ACCESS_SCHEME */, index, buffer, ref bufferSize);
            if (result != 0)
            {
                break;
            }

            var planId = new Guid(buffer);
            plans.Add(new PowerPlan(planId, ReadFriendlyName(planId) ?? planId.ToString(), planId == active));
            index++;
        }

        return plans;
    }

    private static string? ReadFriendlyName(Guid planId)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref planId, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        return PowerReadFriendlyName(IntPtr.Zero, ref planId, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, int value, uint update);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoRef(uint action, uint param, ref int value, uint update);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName")]
    private static partial uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
