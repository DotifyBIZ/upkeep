# ADR-0007: Windows platform code lives in Core, not behind interfaces in the shell

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** `Upkeep.App.Core` (target framework, `Platform/`), `Upkeep.App`, CI coverage gate

## Context

Pulsemap's rule is that `Pulsemap.App.Core` is a plain `net9.0` library with no platform dependency: anything platform-specific (WLAN access, file pickers, PDF rasterization) sits behind an interface in `Core/Abstractions/` and is implemented in the WinUI project. That works there because the platform surface is small and peripheral to the domain — the domain itself is RF propagation math.

Upkeep inverts that. The registry, the service database, the package manager, DISM, the Windows Update Agent, Device Manager, System Restore and the file system *are* the domain. Behind-an-interface-in-the-shell would mean an abstraction for nearly every class in the product, and the implementations — the part where a mistake deletes the wrong directory — would sit in the project that has no coverage bar.

There is also a hard constraint from ADR-0005: the elevated helper must be able to perform these operations without initializing WinUI. Code that lives in `Upkeep.App` and touches `Microsoft.UI.*` cannot be called from a process that must never load it.

## Decision

`Upkeep.App.Core` targets `net9.0-windows10.0.22000.0` and contains the Windows implementations directly — Win32 P/Invoke, WinRT projections, WMI, and processes like `DISM`/`schtasks`/`sc`. It has **no WinUI or Windows App SDK reference**; that boundary is unchanged and still enforced in review.

Inside Core, the split is by testability rather than by platform:

- `Platform/` holds thin adapters that only marshal a call into Windows and return what it said. These may carry `[ExcludeFromCodeCoverage]` with a justification.
- Everything that *decides* something — which files a category includes, whether a service is safe to disable, whether a folder looks like an app's leftovers, how a plan is ordered, what a treemap layout is — is a plain class with no I/O, and is covered by the 80% branch-coverage gate.

Where the shell needs a platform capability that is genuinely UI-bound (file pickers, launching Settings deep links, opening Explorer), the Pulsemap pattern still applies: an interface in `Core/Abstractions/`, implemented in `Upkeep.App`.

## Options Considered

### Platform code in Core, split by testability (chosen)

- Pros: the elevated helper and the tests can both use it; the dangerous decisions are in covered code; a future non-WinUI host (a CLI, another shell) reuses the whole engine rather than reimplementing it.
- Cons: a deliberate deviation from Pulsemap's convention, so the two repos no longer read identically; `[ExcludeFromCodeCoverage]` on adapters needs discipline or it becomes a place to hide logic.

### Pulsemap's rule applied literally (interfaces in Core, implementations in the shell)

- Pros: identical to the sibling project; Core stays trivially portable.
- Cons: an interface per operation for no benefit; the risky code lands in the project exempt from the coverage bar; and the elevated helper would have to live in the WinUI project or duplicate the implementations.

### A third project (`Upkeep.Platform`) between the two

- Pros: keeps Core platform-free and still shares implementations with the helper.
- Cons: a third assembly whose boundary with Core would be arbitrary — every type would need a reason to be on one side or the other, and the brief's layout specifies two projects.

## Consequences

Core is Windows-only and always will be; there is no cross-platform future to protect here, and pretending otherwise would cost real complexity for a portability nobody wants.

The coverage gate is only meaningful if `[ExcludeFromCodeCoverage]` stays honest. Review rule: an excluded class may contain no branch that decides *what* to touch — only the call, its arguments, and mapping the result.

## References

- ADR-0005 (the elevated helper, which cannot load WinUI)
- Pulsemap ADR-0001 (the convention this deviates from, and why it fit there)
- `CLAUDE.md` — "zero WinUI dependency" is the rule, not "zero platform dependency"
