using System.Xml.Linq;

namespace Upkeep.App.Core.Startup;

/// <summary>One scheduled task, as far as the startup list cares about it.</summary>
/// <param name="Path">Full task path, e.g. <c>\GoogleUpdateTaskMachineCore</c>.</param>
/// <param name="Author">Who registered it, where the task says.</param>
/// <param name="Command">The executable it runs.</param>
/// <param name="IsEnabled">Whether Windows will actually run it.</param>
/// <param name="RunsAtLogon">Whether it has a logon trigger — the only kind this page lists.</param>
public sealed record ScheduledTaskEntry(string Path, string? Author, string? Command, bool IsEnabled, bool RunsAtLogon);

/// <summary>
/// Reads the XML that <c>schtasks /query /xml</c> produces.
/// <para>
/// The XML form is parsed rather than the CSV one because CSV output is localized — its column
/// values come back in the user's language, and this machine runs Polish Windows. The XML schema
/// is fixed, so it reads the same everywhere (CLAUDE.md: never parse localized tool output).
/// </para>
/// </summary>
public static class ScheduledTaskXmlParser
{
    /// <summary>
    /// Tasks under this folder belong to Windows. They are not listed: a user disabling a Windows
    /// maintenance task from a cleanup tool is a support call waiting to happen.
    /// </summary>
    public const string WindowsTaskFolderPrefix = @"\Microsoft\";

    /// <summary>
    /// Parses the document, returning only tasks that run at logon. Malformed XML yields an empty
    /// list rather than an exception — this is the output of an external tool, and a startup page
    /// that fails to load because one task is odd is worse than one that lists the rest.
    /// </summary>
    public static IReadOnlyList<ScheduledTaskEntry> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var entries = new List<ScheduledTaskEntry>();

        // Matched by local name: schtasks emits the task-scheduler namespace, and a wrapper around
        // several tasks may not carry the same one.
        foreach (var task in document.Descendants().Where(element => element.Name.LocalName == "Task"))
        {
            var entry = ParseTask(task);
            if (entry is not null && entry.RunsAtLogon && !IsWindowsOwned(entry.Path))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Whether a task belongs to Windows rather than to something the user installed.</summary>
    public static bool IsWindowsOwned(string taskPath) =>
        taskPath.StartsWith(WindowsTaskFolderPrefix, StringComparison.OrdinalIgnoreCase);

    private static ScheduledTaskEntry? ParseTask(XElement task)
    {
        string? uri = Value(task, "URI");
        if (string.IsNullOrWhiteSpace(uri))
        {
            // Without a path there is nothing to toggle later, so the entry is no use.
            return null;
        }

        bool runsAtLogon = task.Descendants().Any(element => element.Name.LocalName == "LogonTrigger");

        // Read from <Settings> specifically, not from anywhere in the task: a trigger carries its
        // own <Enabled> element, and a document-wide search finds the trigger's first — which made
        // a disabled task read as enabled.
        string? enabledValue = task.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Settings")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Enabled")?
            .Value
            .Trim();

        // Absent <Enabled> means enabled: the schema's default, and what Task Scheduler shows.
        bool isEnabled = !string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase);

        string? command = task.Descendants()
            .Where(element => element.Name.LocalName == "Exec")
            .Select(exec => Value(exec, "Command"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new ScheduledTaskEntry(uri, Value(task, "Author"), command, isEnabled, runsAtLogon);
    }

    private static string? Value(XElement parent, string localName) =>
        parent.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim();
}
