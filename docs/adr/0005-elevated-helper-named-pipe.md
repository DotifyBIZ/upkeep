# ADR-0005: One elevated helper per session, over a named pipe

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** `Upkeep.App.Core` (`Elevation/`), every operation that needs administrator rights, `Upkeep.App` (the shell that starts the helper)

## Context

Upkeep runs as a standard user. Some of what it does cannot: cleaning `C:\Windows\Temp` and other users' temp folders, running `DISM /StartComponentCleanup`, changing a service's start type, writing HKLM (startup entries for all users, Windows Update pause/deferral), creating a System Restore point, installing driver updates and rolling drivers back.

Three shapes were available. Elevating the whole app means every user hits a UAC prompt at launch for features most of them won't use, and it means the UI — the biggest attack surface in the product — runs with administrator rights for the whole session. Elevating per action means a UAC prompt for each of possibly dozens of actions in one cleanup run, which trains people to click through prompts. A background service would need administrator rights *at install time*, which contradicts the per-user, no-admin install in ADR-0002.

There is also a specific scenario that shapes the design: a Dotify technician working on a client machine signs in as the client's standard user and enters *their own* administrator credentials at the UAC prompt. The elevated helper then runs as a **different user** than the shell.

## Decision

The shell runs as the signed-in user. The first action that needs administrator rights starts a single elevated helper process — the same `Upkeep.App.exe`, launched with `--elevated-helper` through `ShellExecute`'s `runas` verb, which skips WinUI initialization entirely and runs a message loop instead. The shell talks to it over a local named pipe for the rest of the session, so there is exactly one UAC prompt per session.

**The helper is the pipe server.** It creates the pipe with a random name passed on its command line, `FirstPipeInstance`, `maxNumberOfServerInstances: 1`, and an explicit ACL granting read/write to the SID of the user that owns the shell process (resolved by the helper from the parent PID it was given, not from a SID the shell claims) plus full control to itself. On connect it verifies `GetNamedPipeClientProcessId` matches that same parent PID. It exits when the pipe breaks or the parent process exits.

**The protocol is a closed set of typed operations**, serialized as length-prefixed JSON with a discriminator — never a path, command line or registry key to act on blindly:

- The request names *what kind of work* to do and *which scope* (`CleanJunkCategory { CategoryId }`, `SetServiceStartType { ServiceName, StartType }`, `RemoveLeftovers { AppSnapshot, ItemIds }`, `InstallDriverUpdates { UpdateIds }`, ...).
- For anything that touches a set of files or keys, the helper **re-derives that set itself** by running the same scanner the shell ran, and acts only on the intersection with what was requested. A path the helper's own scan didn't produce is never touched, whatever the shell sends.
- Every operation additionally passes through one policy check (`ActionPolicy`) before it executes: protected roots, protected registry hives and the locked-service list are refused here as well as in the UI.

## Options Considered

### Hybrid — one helper per session, closed operation set (chosen)

- Pros: one UAC prompt; the UI never runs elevated; the elevated surface is a small, enumerable set of operations that can be reviewed as a unit.
- Cons: a live elevated process exists for the rest of the session — genuinely more attack surface than this repo's sibling project has ever had. Mitigated, not eliminated, by the ACL, the PID check, the closed operation set and the re-derivation rule.

### Elevate the entire app at launch

- Pros: simplest code by far; no IPC at all.
- Cons: a UAC prompt for everyone at every launch, including users who only want to see what's taking up disk space; the whole UI, its XAML, its image decoding and its update-check HTTP client all run as administrator.

### Elevate per action

- Pros: smallest elevated window of time.
- Cons: a cleanup run can include a dozen elevated actions; prompt fatigue is a real security cost, and users would rather click "yes" twelve times than read any of them.

### A Windows service

- Pros: no UAC at all after setup; the pattern most commercial tools use.
- Cons: needs administrator rights to install, which kills the per-user install (ADR-0002); leaves a permanently-running privileged component on a client machine after the visit.

## Consequences

An elevated process runs for as long as the app does, once used. It is a service-grade component living in a consumer-grade app, and it is reviewed on those terms: changes to its operation set or its validation need a second reviewer (stated in `CONTRIBUTING.md`).

Because the helper may run as a different user than the shell, **no operation may use ambient per-user state**: `%TEMP%`, `HKEY_CURRENT_USER` and `Environment.SpecialFolder` resolve to the *administrator's* profile inside the helper. All user-scope work stays in the shell; the helper only ever does machine-scope work, with explicit paths it derived itself.

Quarantine has the same split. Items the helper removes go to an administrator-only quarantine under `%ProgramData%\Upkeep\Quarantine` (ACL: Administrators and SYSTEM), never to the user-writable one, so a restore can't be used to plant a file into a protected directory.

Accepted and documented rather than solved: Upkeep installs per-user into a directory the signed-in user can write to (ADR-0002), so code already running as that user could alter the binary that later asks for elevation. This is inherent to per-user installation and is stated plainly in `SECURITY.md`.

## Implementation Notes

- The helper never returns raw exceptions across the pipe; results are typed, with an error code and a message the shell can localize.
- Every helper operation is journaled by the shell (ADR-0006) before it is requested, so an interrupted session can still be reverted.
- If the user dismisses the UAC prompt, that's a normal outcome — `Win32Exception` 1223 becomes "elevation declined", the elevated items stay unchecked, and everything that doesn't need elevation still runs.
- The helper takes one request at a time; the shell serializes them. There is no session state in the helper beyond the open pipe.

## References

- ADR-0002 (per-user, unpackaged install — why a service was not an option)
- ADR-0006 (safety model: preview, quarantine, restore point, session journal)
- `SECURITY.md` (reporting scope, and the accepted per-user install risk)
