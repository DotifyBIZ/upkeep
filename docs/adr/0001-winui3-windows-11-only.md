# ADR-0001: WinUI 3 as the shell, Windows 11 only

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** Application shell (`Upkeep.App`), `Upkeep.App.Core`, packaging

## Context

Upkeep is a Windows maintenance suite. It has to look and behave like part of Windows — a technician runs it in front of a client, and a tool that looks foreign is a tool that doesn't get trusted with deleting things. Dotify's engineering system specifies WinUI 3 / Windows App SDK on .NET 9 as the standard for Windows desktop applications, and Pulsemap (the first Dotify Labs project) already ships on it.

The version floor is a separate question. Pulsemap targets Windows 10 build 19041 because a site-survey tool has to run on whatever laptop is in the van. Upkeep's brief specifies Windows 11 only: Mica is a Windows 11 backdrop, several of the surfaces Upkeep manages (background app permissions, the modern power model, the current Settings deep links) behave differently or don't exist on Windows 10, and supporting both would mean two behaviours to test on every screen.

## Decision

Build `Upkeep.App` on WinUI 3 / Windows App SDK, targeting `net9.0-windows10.0.22000.0` with `TargetPlatformMinVersion` 10.0.22000.0 — Windows 11 21H2 and later, 64-bit. The installer enforces the same floor (`MinVersion=10.0.22000` in `Upkeep.iss`).

Every scan, clean and analysis operation lives in `Upkeep.App.Core`, which has no WinUI dependency (see ADR-0007 for what it *may* depend on).

## Options Considered

### WinUI 3, Windows 11 only (chosen)

- Pros: native Fluent and Mica; matches Dotify's desktop standard and Pulsemap's shell, so the two products feel related; one behaviour to test per screen.
- Cons: excludes Windows 10 machines entirely, including ones a technician may still meet on a client site.

### WinUI 3, Windows 10 1903+ floor (Pulsemap's own target)

- Pros: widest reach; identical to the sibling project's target.
- Cons: Mica silently degrades to a flat backdrop; several managed surfaces differ enough to need per-version branching in exactly the places where being wrong is expensive.

### A newer Windows 11 floor (24H2 / build 26100)

- Pros: only currently-serviced consumer builds; no legacy branches at all.
- Cons: a maintenance tool is most useful on the machines that have been neglected, which are disproportionately the ones still on an older build. Rejected for that reason.

## Consequences

Windows 10 is out of scope for good; a request for it would be a new decision, not a patch. Anything that needs a Windows 11 feature can be used without a capability check, but anything newer than build 22000 still needs one — 22000 is the floor, not the assumption.

## Implementation Notes

- Enforce the boundary in code review: no `Microsoft.WindowsAppSDK` or `Microsoft.UI.*` reference anywhere in `Upkeep.App.Core`.
- The installer's `MinVersion` and the project's `TargetPlatformMinVersion` must move together.

## References

- Pulsemap ADR-0001 (the same decision with a Windows 10 floor, and why)
- ADR-0007 (what platform code Core may contain)
