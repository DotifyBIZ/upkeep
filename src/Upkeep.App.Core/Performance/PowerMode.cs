using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Upkeep.App.Core.Sessions;

namespace Upkeep.App.Core.Performance;

/// <summary>The three power modes in Settings › System › Power.</summary>
public enum PowerMode
{
    BestPowerEfficiency,
    Balanced,
    BestPerformance,
}

/// <summary>Windows keeps a power mode for each power source.</summary>
public enum PowerSource
{
    PluggedIn,
    OnBattery,
}

/// <summary>What the power mode is set to, and whether Windows will honour it.</summary>
/// <param name="PluggedIn">The mode while plugged in; null when Windows wouldn't say or named one Upkeep doesn't know.</param>
/// <param name="OnBattery">The mode on battery; null likewise.</param>
/// <param name="AppliesToActivePlan">Windows applies power mode only under the Balanced plan.</param>
/// <param name="HasBattery">A desktop has no battery, so only the plugged-in mode means anything.</param>
public sealed record PowerModeState(PowerMode? PluggedIn, PowerMode? OnBattery, bool AppliesToActivePlan, bool HasBattery)
{
    /// <summary>
    /// Best power efficiency while plugged in, under a plan where it applies: Windows is holding
    /// the processor back for no battery's sake. The one power-mode case Speed check calls out.
    /// </summary>
    public bool IsThrottledWhilePluggedIn => AppliesToActivePlan && PluggedIn == PowerMode.BestPowerEfficiency;
}

/// <summary>The GUIDs Windows uses for each mode (ADR-0010).</summary>
public static class PowerModes
{
    public static readonly Guid BestPowerEfficiencyId = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid BalancedId = Guid.Empty;
    public static readonly Guid BestPerformanceId = new("ded574b5-45a0-4f42-8737-46345c09c238");

    /// <summary>The Balanced power plan — the only plan power mode applies under.</summary>
    public static readonly Guid BalancedPlanId = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    public static Guid ToId(PowerMode mode) => mode switch
    {
        PowerMode.BestPowerEfficiency => BestPowerEfficiencyId,
        PowerMode.BestPerformance => BestPerformanceId,
        _ => BalancedId,
    };

    public static PowerMode? FromId(Guid id) =>
        id == BestPowerEfficiencyId ? PowerMode.BestPowerEfficiency
        : id == BalancedId ? PowerMode.Balanced
        : id == BestPerformanceId ? PowerMode.BestPerformance
        : null;

    /// <summary>The setting id journaled for each source; on-disk data, so it never changes.</summary>
    public static string SettingId(PowerSource source) =>
        source == PowerSource.PluggedIn ? PerformanceSettingIds.PowerModePluggedIn : PerformanceSettingIds.PowerModeOnBattery;
}

/// <summary>Reads and sets Windows' power mode, for the current user, per power source.</summary>
public interface IPowerModeSettings
{
    PowerModeState Read();

    bool SetMode(PowerSource source, PowerMode mode, out string? failure);
}

/// <inheritdoc />
[ExcludeFromCodeCoverage(Justification = "Thin powrprof/kernel32 wrapper; the decisions built on it are tested in the view model and SessionReverter.")]
public sealed partial class PowerModeSettings : IPowerModeSettings
{
    /// <summary>BATTERY_FLAG_NO_BATTERY in SYSTEM_POWER_STATUS.</summary>
    private const byte NoSystemBattery = 128;

    public PowerModeState Read()
    {
        PowerMode? pluggedIn = PowerGetUserConfiguredACPowerMode(out Guid ac) == 0 ? PowerModes.FromId(ac) : null;
        PowerMode? onBattery = PowerGetUserConfiguredDCPowerMode(out Guid dc) == 0 ? PowerModes.FromId(dc) : null;

        return new PowerModeState(pluggedIn, onBattery, ActivePlan() == PowerModes.BalancedPlanId, HasBattery());
    }

    public bool SetMode(PowerSource source, PowerMode mode, out string? failure)
    {
        Guid id = PowerModes.ToId(mode);
        uint result = source == PowerSource.PluggedIn
            ? PowerSetUserConfiguredACPowerMode(ref id)
            : PowerSetUserConfiguredDCPowerMode(ref id);

        failure = result == 0 ? null : $"Windows refused the power mode ({result}).";
        return result == 0;
    }

    private static Guid ActivePlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer) != 0 || pointer == IntPtr.Zero)
        {
            return Guid.Empty;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    private static bool HasBattery() =>
        GetSystemPowerStatus(out SystemPowerStatus status) && (status.BatteryFlag & NoSystemBattery) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredACPowerMode(ref Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetUserConfiguredDCPowerMode(ref Guid powerModeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
