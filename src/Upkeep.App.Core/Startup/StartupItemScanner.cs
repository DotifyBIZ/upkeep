using Upkeep.App.Core.Platform;

namespace Upkeep.App.Core.Startup;

/// <summary>Lists everything that starts with Windows.</summary>
public interface IStartupItemScanner
{
    Task<IReadOnlyList<StartupItem>> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the four places Windows starts things from: the Run and RunOnce keys (per-user and
/// machine-wide, in both registry views), the Startup folders, and scheduled tasks with a logon
/// trigger.
/// <para>
/// Enabled/disabled state comes from the same StartupApproved values Task Manager writes, so the
/// two tools always agree about what is on.
/// </para>
/// </summary>
public sealed class StartupItemScanner : IStartupItemScanner
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunKeyWow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedStartupFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private static readonly TimeSpan TaskQueryTimeout = TimeSpan.FromSeconds(30);

    private readonly IRegistryProbe _registry;
    private readonly IWellKnownPaths _paths;
    private readonly IWindowsToolRunner _toolRunner;

    public StartupItemScanner(IRegistryProbe registry, IWellKnownPaths paths, IWindowsToolRunner toolRunner)
    {
        _registry = registry;
        _paths = paths;
        _toolRunner = toolRunner;
    }

    public async Task<IReadOnlyList<StartupItem>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        AddRunKeyItems(items, RegistryHiveName.CurrentUser, RunKey, ApprovedRun, StartupSource.RunKeyCurrentUser, requiresElevation: false);
        AddRunKeyItems(items, RegistryHiveName.LocalMachine, RunKey, ApprovedRun, StartupSource.RunKeyAllUsers, requiresElevation: true);

        // 32-bit installers on 64-bit Windows land here, and Task Manager tracks their state in a
        // separate StartupApproved key.
        AddRunKeyItems(items, RegistryHiveName.LocalMachine, RunKeyWow, ApprovedRun32, StartupSource.RunKeyAllUsers, requiresElevation: true);

        AddRunOnceItems(items, RegistryHiveName.CurrentUser, requiresElevation: false);
        AddRunOnceItems(items, RegistryHiveName.LocalMachine, requiresElevation: true);

        AddStartupFolderItems(items, StartupFolderPath(_paths.RoamingAppData), requiresElevation: false);
        AddStartupFolderItems(items, StartupFolderPath(_paths.ProgramData), requiresElevation: true);

        items.AddRange(await ReadLogonTasksAsync(cancellationToken));

        return [.. items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string StartupFolderPath(string root) =>
        Path.Combine(root, "Microsoft", "Windows", "Start Menu", "Programs", "Startup");

    private void AddRunKeyItems(
        List<StartupItem> items,
        RegistryHiveName hive,
        string keyPath,
        string approvedKeyPath,
        StartupSource source,
        bool requiresElevation)
    {
        foreach (string valueName in _registry.GetValueNames(hive, keyPath))
        {
            // Task Manager records state per-user even for machine-wide entries, which is why the
            // approval is always read from the current user's hive.
            byte[]? approval = _registry.GetBinaryValue(RegistryHiveName.CurrentUser, approvedKeyPath, valueName);

            items.Add(new StartupItem
            {
                Id = valueName,
                Name = valueName,
                Command = _registry.GetStringValue(hive, keyPath, valueName),
                Source = source,
                IsEnabled = StartupApprovedValue.IsEnabled(approval),
                DisabledAt = StartupApprovedValue.DisabledAt(approval),
                RequiresElevation = requiresElevation,
                Hive = hive,
                RegistryKeyPath = keyPath,
            });
        }
    }

    private void AddRunOnceItems(List<StartupItem> items, RegistryHiveName hive, bool requiresElevation)
    {
        foreach (string valueName in _registry.GetValueNames(hive, RunOnceKey))
        {
            items.Add(new StartupItem
            {
                Id = valueName,
                Name = valueName,
                Command = _registry.GetStringValue(hive, RunOnceKey, valueName),
                Source = StartupSource.RunOnce,

                // RunOnce entries delete themselves after running; there is no approval to read
                // and nothing to toggle, only to remove.
                IsEnabled = true,
                RequiresElevation = requiresElevation,
                Hive = hive,
                RegistryKeyPath = RunOnceKey,
            });
        }
    }

    private void AddStartupFolderItems(List<StartupItem> items, string folder, bool requiresElevation)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            byte[]? approval = _registry.GetBinaryValue(RegistryHiveName.CurrentUser, ApprovedStartupFolder, name);

            items.Add(new StartupItem
            {
                Id = name,
                Name = Path.GetFileNameWithoutExtension(file),
                Command = file,
                Source = StartupSource.StartupFolder,
                IsEnabled = StartupApprovedValue.IsEnabled(approval),
                DisabledAt = StartupApprovedValue.DisabledAt(approval),
                RequiresElevation = requiresElevation,
                Hive = RegistryHiveName.CurrentUser,
                RegistryKeyPath = ApprovedStartupFolder,
            });
        }
    }

    /// <summary>
    /// Scheduled tasks come from <c>schtasks /query /xml</c> — the XML form, because the CSV one is
    /// localized and this repo runs on Polish Windows daily.
    /// </summary>
    private async Task<IReadOnlyList<StartupItem>> ReadLogonTasksAsync(CancellationToken cancellationToken)
    {
        string schtasks = Path.Combine(_paths.WindowsDirectory, "System32", "schtasks.exe");
        var result = await _toolRunner.RunAsync(schtasks, ["/query", "/xml", "ONE"], TaskQueryTimeout, cancellationToken);

        if (!result.Succeeded)
        {
            return [];
        }

        return
        [
            .. ScheduledTaskXmlParser.Parse(result.Output).Select(task => new StartupItem
            {
                Id = task.Path,
                Name = task.Path.TrimStart('\\'),
                Publisher = task.Author,
                Command = task.Command,
                Source = StartupSource.ScheduledTask,
                IsEnabled = task.IsEnabled,

                // A task registered outside the user's own folder needs rights to change.
                RequiresElevation = true,
                TaskPath = task.Path,
            }),
        ];
    }
}
