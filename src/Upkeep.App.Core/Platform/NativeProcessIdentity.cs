using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Upkeep.App.Core.Platform;

/// <summary>
/// The two Windows questions the elevated helper has to answer for itself: who owns the process
/// that started me, and who is actually connected to my pipe. Both are asked of the kernel rather
/// than taken from the command line, because the command line is written by the caller.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin P/Invoke wrapper; behaviour belongs to Windows and is exercised by the helper's own integration path.")]
internal static partial class NativeProcessIdentity
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;
    private const int TokenUserInformationClass = 1;

    /// <summary>Resolves the SID of the user account that owns <paramref name="processId"/>.</summary>
    /// <remarks>
    /// The helper may run as a *different* user than the shell — a technician entering their own
    /// administrator credentials at the UAC prompt on a client's machine (ADR-0005). So the pipe's
    /// ACL is built from the shell process's real owner, looked up here, rather than from the
    /// helper's own identity or a SID passed as an argument.
    /// </remarks>
    public static SecurityIdentifier GetProcessUserSid(int processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open process {processId}.");
        }

        if (!OpenProcessToken(process, TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open the token of process {processId}.");
        }

        using (token)
        {
            GetTokenInformation(token, TokenUserInformationClass, IntPtr.Zero, 0, out int needed);
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, TokenUserInformationClass, buffer, needed, out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the token user.");
                }

                // TOKEN_USER is a SID_AND_ATTRIBUTES whose first field is the SID pointer.
                IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                return new SecurityIdentifier(sidPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>The process id at the other end of a connected pipe, straight from the kernel.</summary>
    public static int GetPipeClientProcessId(SafePipeHandle pipeHandle)
    {
        if (!GetNamedPipeClientProcessId(pipeHandle, out uint clientProcessId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not identify the pipe client process.");
        }

        return (int)clientProcessId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, int desiredAccess, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
