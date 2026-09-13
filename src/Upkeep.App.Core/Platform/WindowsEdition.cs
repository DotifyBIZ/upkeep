namespace Upkeep.App.Core.Platform;

/// <summary>Which Windows edition this is, as far as Upkeep needs to care.</summary>
public enum WindowsEditionKind
{
    /// <summary>Home and its variants. Ignores the update deferral policies entirely.</summary>
    Home,

    Professional,

    Enterprise,

    Education,

    /// <summary>Anything else — Server, IoT, an edition that didn't exist when this was written.</summary>
    Other,
}

/// <summary>What the edition means for the controls Upkeep offers.</summary>
/// <param name="EditionId">The raw <c>EditionID</c> value, kept for the log.</param>
public sealed record WindowsEditionInfo(string EditionId, WindowsEditionKind Kind)
{
    /// <summary>
    /// Whether the update deferral policies do anything here. Home reads the policy keys and
    /// ignores them, so a deferral control on Home would be a switch wired to nothing.
    /// </summary>
    public bool SupportsUpdateDeferral => Kind is WindowsEditionKind.Professional
        or WindowsEditionKind.Enterprise
        or WindowsEditionKind.Education;
}

/// <summary>
/// Reads which edition of Windows this is.
/// <para>
/// From <c>EditionID</c> rather than the display name: the display name is localized, and this
/// machine runs Polish Windows (CLAUDE.md). <c>EditionID</c> is a stable identifier.
/// </para>
/// </summary>
public sealed class WindowsEditionProbe
{
    public const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public const string EditionValueName = "EditionID";

    private readonly IRegistryProbe _registry;

    public WindowsEditionProbe(IRegistryProbe registry) => _registry = registry;

    public WindowsEditionInfo Read()
    {
        string? editionId = _registry.GetStringValue(RegistryHiveName.LocalMachine, CurrentVersionKey, EditionValueName);
        return new WindowsEditionInfo(editionId ?? string.Empty, Classify(editionId));
    }

    /// <summary>
    /// Maps an <c>EditionID</c> to what Upkeep does about it. Unknown editions land in
    /// <see cref="WindowsEditionKind.Other"/>, which offers no deferral — showing a control that
    /// might do nothing is worse than not showing it.
    /// </summary>
    public static WindowsEditionKind Classify(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
        {
            return WindowsEditionKind.Other;
        }

        // "Core" is what Home reports, in every variant: Core, CoreN, CoreSingleLanguage.
        if (editionId.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsEditionKind.Home;
        }

        if (editionId.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsEditionKind.Enterprise;
        }

        if (editionId.Contains("Education", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsEditionKind.Education;
        }

        // Checked last: ProfessionalEducation and ProfessionalWorkstation both contain it, and the
        // more specific answers above are the ones worth reporting.
        return editionId.Contains("Professional", StringComparison.OrdinalIgnoreCase)
            ? WindowsEditionKind.Professional
            : WindowsEditionKind.Other;
    }
}
