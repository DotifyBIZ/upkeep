using System.Text.Json.Serialization;

namespace Upkeep.App.Core.Elevation;

/// <summary>
/// The complete set of operations the elevated helper will perform. This hierarchy *is* the
/// security boundary described in docs/adr/0005-elevated-helper-named-pipe.md: a request names a
/// kind of work and a scope, never a path, registry key or command line to act on blindly. Every
/// handler re-derives its own candidate set and acts only on the intersection with what was asked.
/// <para>
/// Adding a derived type here means widening what a process running as administrator will do on
/// someone else's machine. CONTRIBUTING.md requires a second reviewer for that.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(PingRequest), "ping")]
[JsonDerivedType(typeof(ScanJunkCategoryRequest), "scan-junk")]
[JsonDerivedType(typeof(CleanJunkCategoryRequest), "clean-junk")]
[JsonDerivedType(typeof(CreateRestorePointRequest), "create-restore-point")]
public abstract record HelperRequest
{
    /// <summary>Correlates a response with its request; the shell sends one request at a time.</summary>
    public int RequestId { get; init; }
}

/// <summary>Confirms the helper is alive and the pipe is usable. Does nothing else, deliberately:
/// the shell uses it to prove elevation succeeded before showing anything as available.</summary>
public sealed record PingRequest : HelperRequest;

/// <summary>
/// Measures one machine-wide junk category. The helper walks the paths that category is defined
/// as — the shell names the category, never a path.
/// </summary>
public sealed record ScanJunkCategoryRequest(Cleanup.JunkCategoryId CategoryId) : HelperRequest;

/// <summary>
/// Cleans one machine-wide junk category.
/// <para>
/// <paramref name="Paths"/> is what the user saw and approved in the preview, not an instruction
/// to delete arbitrary paths: the helper re-derives the category's own file list and removes only
/// the intersection of the two. A path the helper's own scan didn't produce is never touched,
/// whatever arrives here (ADR-0005).
/// </para>
/// </summary>
public sealed record CleanJunkCategoryRequest(Cleanup.JunkCategoryId CategoryId, IReadOnlyList<string> Paths) : HelperRequest;

/// <summary>
/// Turns System Protection on if it is off, then creates a restore point — or reports the recent
/// one Windows would rather keep (ADR-0006). Administrator-only, which is why it lives here.
/// </summary>
public sealed record CreateRestorePointRequest(string Description) : HelperRequest;

/// <summary>
/// Result of one helper operation. Failures cross the pipe as an error code the shell can
/// localize — never as a raw exception or a stack trace.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "result", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(HelperOkResponse), "ok")]
[JsonDerivedType(typeof(HelperErrorResponse), "error")]
[JsonDerivedType(typeof(JunkScanResponse), "junk-scan")]
[JsonDerivedType(typeof(JunkCleanResponse), "junk-clean")]
[JsonDerivedType(typeof(RestorePointResponse), "restore-point")]
public abstract record HelperResponse
{
    public int RequestId { get; init; }
}

public sealed record HelperOkResponse : HelperResponse;

/// <summary>What a machine-wide category's scan found, in the same shape the shell's own scans use.</summary>
public sealed record JunkScanResponse(Cleanup.JunkCategoryScan Scan) : HelperResponse;

/// <summary>What cleaning a machine-wide category actually managed to remove.</summary>
public sealed record JunkCleanResponse(long FreedBytes, long ItemsRemoved, long ItemsSkipped) : HelperResponse;

/// <summary>What Windows did when asked for a restore point.</summary>
public sealed record RestorePointResponse(Safety.RestorePointStatus Status, string? Description, bool ProtectionWasEnabled) : HelperResponse;

/// <summary>
/// A failed operation. <paramref name="Code"/> is one of <see cref="HelperErrorCodes"/> — a stable
/// identifier the shell maps to a localized message, so the helper never decides user-facing text.
/// </summary>
public sealed record HelperErrorResponse(string Code, string Detail) : HelperResponse;

public static class HelperErrorCodes
{
    /// <summary>The request deserialized to a type this helper build doesn't implement.</summary>
    public const string UnsupportedOperation = "unsupported_operation";

    /// <summary>The request was well-formed but refused by the helper's own policy check.</summary>
    public const string RefusedByPolicy = "refused_by_policy";

    /// <summary>Windows refused the operation itself (access denied, locked file, busy service).</summary>
    public const string WindowsRefused = "windows_refused";

    /// <summary>The request could not be parsed at all.</summary>
    public const string MalformedRequest = "malformed_request";
}
