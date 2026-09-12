using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using Microsoft.Win32;

namespace Upkeep.App.Core.Platform;

/// <summary>
/// Finds a user's profile folder from their SID, the way Windows itself records it.
/// <para>
/// The elevated helper needs this because it cannot ask its own environment: with
/// over-the-shoulder elevation the helper runs as the technician's administrator account while the
/// person using Upkeep is someone else entirely (ADR-0005). %USERPROFILE% inside the helper is the
/// wrong answer, so the shell's owner SID is resolved through the registry instead.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads a machine-specific registry key; the caller's use of the result is tested against a substitute.")]
public static class UserProfileResolver
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    /// <summary>
    /// The profile directory for <paramref name="userSid"/>, or null when Windows has no entry —
    /// in which case nothing is excluded rather than the wrong thing being excluded.
    /// </summary>
    public static string? TryGetProfilePath(SecurityIdentifier userSid)
    {
        ArgumentNullException.ThrowIfNull(userSid);

        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            using var profile = profileList?.OpenSubKey(userSid.Value);

            return profile?.GetValue("ProfileImagePath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
