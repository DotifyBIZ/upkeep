# ADR-0003: Local diagnostic logging, not telemetry

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** `Upkeep.App.Core` (`Logging/`), every failure path, Settings page

## Context

Upkeep promises no telemetry, no account and no server. That stance is not up for revision here.

Separately, a maintenance tool fails in ways the user cannot describe. "It said it couldn't delete something", "it froze while scanning D:", "the service didn't turn off" are all reports that are unactionable without knowing which path, which error code, which service. The app also runs unattended-ish operations — a long scan, a batch of elevated actions — where a single item failing must not stop the run, which means failures need somewhere to go other than a dialog.

## Decision

A local, rolling, plain-text log (`IAppLogger` / `FileAppLogger` in `Upkeep.App.Core/Logging/`) writing ERROR/WARN/INFO lines to `%LocalAppData%\Upkeep\Logs\upkeep-{yyyy-MM-dd}.log`, pruned after 30 days. Nothing is transmitted anywhere by the app. Settings has an "Open logs folder" button; the user decides whether to attach the file to a bug report.

The elevated helper logs to the same folder structure under its own profile, and returns error codes to the shell, which logs them with the operation that caused them — so one session's story is readable from the user's log even when the work happened in the other process.

## Options Considered

### Local-only log file, manually shared (chosen)

- Pros: adds no network surface, so it doesn't touch the "nothing phones home" line at all; the user stays in control of what leaves the machine; enough to diagnose the per-item failures this app generates by design.
- Cons: depends on the user finding and sending the file.

### Automatic crash/error reporting to a backend

- Pros: catches failures nobody reports.
- Cons: needs a backend, contradicts the product's stated privacy position, and would require its own decision and review. Not a Phase 1 call to make quietly.

### No logging

- Pros: nothing to build.
- Cons: leaves "it didn't work" unanswerable, on a tool whose failures are routine and per-item by design.

## Consequences

Every catch block logs rather than swallowing. Logging itself must never throw — a full disk or a permissions problem costs one log line, not the caller's own error handling.

If telemetry is ever proposed, it is a separate decision with its own ADR; this log is not a stepping stone toward it.

## Implementation Notes

- Lives in Core, not the shell: Core's own operations need to log, and plain file I/O needs no UI.
- `%LocalAppData%`, not Documents — this is app diagnostic data, not user data.
- Exactly three levels (Error/Warning/Info). No Debug/Trace added speculatively.
- Log lines carry paths and service names — the things a user's report needs — and never file *contents*.

## References

- Pulsemap ADR-0003 (same decision, same reasoning)
- ADR-0004 (the one network call this project does make)
