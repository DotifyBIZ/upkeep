namespace Upkeep.App.Core.Services;

/// <summary>
/// How much confidence Upkeep is willing to express about turning a service off. Services are the
/// easiest place in this product to break a machine, so the tiers are deliberately blunt.
/// </summary>
public enum ServiceTier
{
    /// <summary>
    /// On the curated list: what stops working is known, stated on the row, and limited. Nothing
    /// reaches this tier by inference — only by being named in <see cref="ServiceCatalog"/>.
    /// </summary>
    CommonlySafe,

    /// <summary>Not from Microsoft — usually an app's updater or helper. Shown neutrally: it's the
    /// user's software, and only they know whether they still want it.</summary>
    ThirdParty,

    /// <summary>Part of Windows, not on the curated list. Changeable, with a warning first.</summary>
    LeaveAlone,

    /// <summary>Windows needs it to start, sign in, stay secure or update. Upkeep won't change it.</summary>
    Locked,
}

/// <summary>One curated service, and the honest one-line answer to "what stops working?".</summary>
/// <param name="ServiceName">The service key name, as Windows knows it.</param>
/// <param name="WhatBreaksKey">Resource key for what the user loses by disabling it.</param>
public sealed record CuratedService(string ServiceName, string WhatBreaksKey);

/// <summary>
/// The service lists Upkeep is prepared to stand behind.
/// <para>
/// The "commonly safe" set is short on purpose and was approved item by item. Most of these are
/// demand-start services, so turning them off is tidiness rather than speed — the UI says that
/// rather than implying a performance win.
/// </para>
/// </summary>
public static class ServiceCatalog
{
    /// <summary>
    /// Services Upkeep will mark as commonly safe to disable. Each carries its own "what breaks"
    /// line; none of them is inferred.
    /// </summary>
    public static readonly IReadOnlyList<CuratedService> CommonlySafe =
    [
        new("DiagTrack", "ServiceBreaksDiagTrack"),
        new("MapsBroker", "ServiceBreaksMapsBroker"),
        new("XblAuthManager", "ServiceBreaksXbox"),
        new("XblGameSave", "ServiceBreaksXbox"),
        new("XboxNetApiSvc", "ServiceBreaksXbox"),
        new("XboxGipSvc", "ServiceBreaksXboxAccessories"),
        new("Fax", "ServiceBreaksFax"),
        new("RetailDemo", "ServiceBreaksRetailDemo"),
        new("WpcMonSvc", "ServiceBreaksParentalControls"),
        new("SEMgrSvc", "ServiceBreaksPayments"),
        new("wisvc", "ServiceBreaksInsider"),
        new("RemoteRegistry", "ServiceBreaksRemoteRegistry"),
    ];

    /// <summary>
    /// Services Upkeep refuses to change, whatever else is true about them. Boot, sign-in,
    /// networking, security and servicing: the places where being wrong means a machine that
    /// doesn't start or can't be updated.
    /// </summary>
    public static readonly IReadOnlySet<string> Locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Boot and core plumbing
        "DcomLaunch", "RpcSs", "RpcEptMapper", "LSM", "PlugPlay", "Power", "BrokerInfrastructure",
        "CoreMessagingRegistrar", "SystemEventsBroker", "DeviceInstall", "StateRepository", "TimeBrokerSvc",
        // Sign-in and identity
        "ProfSvc", "UserManager", "SamSs", "KeyIso", "VaultSvc", "gpsvc",
        // Security
        "BFE", "mpssvc", "WinDefend", "WdNisSvc", "Sense", "SgrmBroker", "SecurityHealthService", "wscsvc", "CryptSvc",
        // Networking
        "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "Wcmsvc", "LanmanWorkstation", "LanmanServer",
        // Servicing and management
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "TrustedInstaller", "msiserver", "Winmgmt", "Schedule", "EventLog", "EventSystem",
        // Audio and shell essentials people notice immediately
        "Audiosrv", "AudioEndpointBuilder", "ShellHWDetection", "AppXSvc", "ClipSVC",
    };

    public static CuratedService? FindCurated(string serviceName) =>
        CommonlySafe.FirstOrDefault(service => service.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What Windows reports about one service, as the classifier needs it.</summary>
/// <param name="Name">Service key name.</param>
/// <param name="DisplayName">The name a person sees in services.msc.</param>
/// <param name="StartType">Automatic, Automatic (delayed), Manual, Disabled.</param>
/// <param name="IsRunning">Whether it is running right now.</param>
/// <param name="IsMicrosoft">Whether the binary behind it comes from Microsoft.</param>
/// <param name="HasRunningDependents">Whether anything currently running depends on it.</param>
/// <param name="IsDriver">Kernel and file-system drivers are never listed as services here.</param>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string StartType,
    bool IsRunning,
    bool IsMicrosoft,
    bool HasRunningDependents = false,
    bool IsDriver = false);

/// <summary>
/// Decides which tier a service belongs in. Pure, and the single place the rules live — the UI
/// only renders the answer, and the elevated helper re-checks it before changing anything.
/// </summary>
public static class ServiceClassifier
{
    public static ServiceTier Classify(ServiceInfo service)
    {
        ArgumentNullException.ThrowIfNull(service);

        // Anything with something running on top of it stops that too. Whatever list it is on,
        // that makes it a bad suggestion.
        if (ServiceCatalog.Locked.Contains(service.Name) || service.HasRunningDependents)
        {
            return ServiceTier.Locked;
        }

        if (ServiceCatalog.FindCurated(service.Name) is not null)
        {
            return ServiceTier.CommonlySafe;
        }

        return service.IsMicrosoft ? ServiceTier.LeaveAlone : ServiceTier.ThirdParty;
    }

    /// <summary>Whether Upkeep will let this service's start type be changed at all.</summary>
    public static bool CanChange(ServiceInfo service) => Classify(service) != ServiceTier.Locked;

    /// <summary>
    /// The "what breaks" line for a curated service; null for everything else, where Upkeep has
    /// nothing honest to say and the UI shows the tier's general wording instead.
    /// </summary>
    public static string? WhatBreaksKey(ServiceInfo service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return ServiceCatalog.FindCurated(service.Name)?.WhatBreaksKey;
    }
}
