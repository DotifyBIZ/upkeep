using System.Diagnostics.CodeAnalysis;

namespace Upkeep.App.Core.Drivers;

/// <summary>One driver update Windows Update is offering.</summary>
/// <param name="Title">The update's own title, which usually names the device and the vendor.</param>
/// <param name="DriverModel">The device model the update targets, where the update declares one.</param>
public sealed record DriverUpdate(string Title, string? DriverModel);

/// <summary>What a search of Windows Update found, or why it found nothing.</summary>
/// <param name="Succeeded">False when Windows Update could not be searched at all.</param>
public sealed record DriverUpdateSearchResult(bool Succeeded, IReadOnlyList<DriverUpdate> Updates)
{
    public static readonly DriverUpdateSearchResult Unavailable = new(false, []);
}

/// <summary>Asks Windows Update which drivers have something newer. Read-only, by decision.</summary>
public interface IDriverUpdateSearch
{
    DriverUpdateSearchResult Search(CancellationToken cancellationToken);
}

/// <summary>
/// A read-only search of the Windows Update Agent for driver updates (ADR-0009).
/// <para>
/// Late-bound COM, because the project references no update-agent interop assembly and adding one
/// to install nothing would be a poor trade. The surface used here is deliberately tiny: create a
/// session, create a searcher, run one query, read titles. Upkeep never downloads or installs —
/// that is handed to Windows, which already knows how to finish after a restart.
/// </para>
/// <para>
/// Every failure is the same answer: Windows Update could not be searched. A machine with the
/// service disabled, a managed network, or no connection is a normal case, not an error worth
/// showing a stack trace for.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Late-bound COM against the Windows Update Agent; it cannot run without a real update service, and it makes no decisions of its own.")]
public sealed class DriverUpdateSearch : IDriverUpdateSearch
{
    /// <summary>Drivers that are not installed yet — the only thing this asks for.</summary>
    private const string DriverCriteria = "IsInstalled=0 and Type='Driver'";

    private const string UpdateSessionProgId = "Microsoft.Update.Session";

    public DriverUpdateSearchResult Search(CancellationToken cancellationToken)
    {
        var sessionType = Type.GetTypeFromProgID(UpdateSessionProgId);
        if (sessionType is null)
        {
            return DriverUpdateSearchResult.Unavailable;
        }

        object? session = null;

        try
        {
            session = Activator.CreateInstance(sessionType);
            if (session is null)
            {
                return DriverUpdateSearchResult.Unavailable;
            }

            dynamic searcher = ((dynamic)session).CreateUpdateSearcher();

            // Windows Update's own server, not a WSUS-managed one: asking a managed machine's
            // server for drivers it does not publish returns nothing useful.
            searcher.ServerSelection = 2;
            searcher.Online = true;

            cancellationToken.ThrowIfCancellationRequested();

            dynamic result = searcher.Search(DriverCriteria);
            var updates = new List<DriverUpdate>();

            foreach (dynamic update in result.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                updates.Add(new DriverUpdate((string)update.Title, ReadDriverModel(update)));
            }

            return new DriverUpdateSearchResult(true, updates);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // COMException, UnauthorizedAccessException, a binder failure on an unexpected shape —
            // all of them mean the same thing to the page.
            return DriverUpdateSearchResult.Unavailable;
        }
        finally
        {
            if (session is not null && System.Runtime.InteropServices.Marshal.IsComObject(session))
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(session);
            }
        }
    }

    /// <summary>
    /// Only driver updates carry DriverModel, and even then not always; a missing one is normal
    /// rather than a failure.
    /// </summary>
    private static string? ReadDriverModel(dynamic update)
    {
        try
        {
            return update.DriverModel as string;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }
}
