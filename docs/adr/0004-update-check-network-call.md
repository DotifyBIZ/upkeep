# ADR-0004: The in-app update check, and the other traffic Upkeep causes

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** `Upkeep.App` (`Services/GitHubUpdateCheckService`), `Upkeep.App.Core` (`Abstractions/IUpdateCheckService`, `Updates/`, `Settings/`), README, Settings page

## Context

Upkeep is distributed as an unsigned, unpackaged installer (ADR-0002), so there is no Store or MSIX mechanism to tell a user a newer version exists. Without a check, someone can sit on an old build indefinitely — which matters more for this product than most, because its bugs delete things.

The product also states that nothing about the user or their machine leaves the PC. Any outbound call needs to be named rather than assumed harmless.

## Decision

One check, on launch, against GitHub's public release list: `GET https://api.github.com/repos/DotifyBIZ/upkeep/releases/latest`, comparing `tag_name` with the running assembly's version. If a newer version exists, Home shows a dismissible banner linking to the release. Nothing downloads, nothing installs.

The request carries a User-Agent and nothing else — no account, machine or usage data. It is still an outbound call, so it gets a visible Settings toggle (`Check for updates on launch`, default on) rather than being folded into "diagnostics".

**Two other things cause traffic, and are named rather than implied:**

- **Driver updates** are fetched through the Windows Update Agent when the user opens or refreshes the Drivers page. That is Windows' own update channel, using the machine's existing configuration (including WSUS/Intune where an organization manages it). Upkeep adds no endpoint of its own. No toggle: opening the page is the request.
- **Nothing else.** No cache, no catalogue, no rule list is fetched at runtime; every classification the app makes ships inside the binary.

## Options Considered

### GitHub release check, on by default, opt-out (chosen)

- Pros: no backend; cheap to reason about — a version string in, a boolean out; reaches the people who need it, since almost nobody visits Settings before their first launch.
- Cons: a network call the product would otherwise not make; requires the README's privacy claim to be precise rather than absolute.

### Opt-in (default off)

- Pros: the strictest reading of "nothing leaves your machine".
- Cons: effectively nobody enables it, so the feature exists without helping anyone; and the privacy cost of a non-identifying version check is close to zero.

### A silent background updater (Velopack, Squirrel)

- Pros: removes the manual download step.
- Cons: much larger failure surface, on an app that can't yet sign its installer; auto-updating a tool with administrator reach is a bigger promise than this project is ready to make. Deferred, not rejected forever.

### No check at all

- Pros: zero network surface.
- Cons: users stay on old builds of a tool that deletes files.

## Consequences

The README's privacy section distinguishes telemetry, accounts and servers (absent) from these two specific, disclosed cases. The literal "zero network calls" reading does not hold; the substantive claim — nothing about you or this machine leaves it — does.

`scripts/build-installer.ps1` passes `-p:Version=$Version` to `dotnet publish`, so the running assembly carries the real release version. Without that, the comparison would be meaningless regardless of design.

## Implementation Notes

- `IUpdateCheckService` in Core's `Abstractions/`; the concrete `GitHubUpdateCheckService` lives in `Upkeep.App` because it needs `IHttpClientFactory` — never a directly constructed `HttpClient`.
- Version comparison (`SemanticVersionComparer`) is pure and lives in Core, so it is tested without a network.
- The call has an explicit 10-second timeout and a 1 MB response cap; it runs on the Home page load path and must never delay or block it.
- A failed check (offline, GitHub down, rate-limited, malformed) is treated exactly like "no update available": logged as a warning, never surfaced as an error.

## References

- ADR-0002 (no Store/MSIX update mechanism available)
- ADR-0003 (local logging — the previous "does this touch our privacy claim" decision)
