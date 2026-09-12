# ADR-0002: InnoSetup installer instead of MSIX

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** Packaging, distribution, release CI, the elevation model

## Context

Dotify's C# standard lists "MSIX package deployment (not file-copy distribution)" as a non-negotiable for WinUI 3 desktop applications. Upkeep, like Pulsemap before it, is run on client-site machines whose Group Policy and security posture Dotify does not control — and unlike Pulsemap, it is also a tool a member of the public downloads and runs once to clean up their own PC.

MSIX has a hard requirement a traditional installer doesn't: Windows will not install an unsigned MSIX at all, and a self-signed one additionally needs Developer Mode or the `AllowAllTrustedApps` policy enabled on the target machine. Dotify holds no code-signing certificate, and the free route (SignPath Foundation) requires an OSI-approved license — PolyForm Shield is deliberately not one.

MSIX is also a poor fit for what this app *does*. A packaged app runs with virtualized file-system and registry views and a restricted token; Upkeep needs to enumerate and edit HKLM, clean `C:\Windows\Temp` and the component store, change service start types, and launch an elevated helper process (ADR-0005). Those are exactly the operations the MSIX container exists to prevent.

## Decision

Package Upkeep with **InnoSetup** (`Upkeep.iss`), published as a GitHub Release asset with a SHA-256 sidecar. Per-user install under `{localappdata}\Programs\Upkeep`, `PrivilegesRequired=lowest` — **no administrator rights to install**. Administrator rights are requested at runtime, once per session, only for the actions that need them.

The app is built unpackaged (`WindowsPackageType=None`) and self-contained (`WindowsAppSDKSelfContained=true`), so there is no Windows App SDK runtime prerequisite.

Unsigned for now, matching Pulsemap's call: SmartScreen will warn until download reputation builds, but nothing blocks installation.

## Options Considered

### InnoSetup, per-user, unsigned (chosen)

- Pros: no signing requirement to install; no dependency on the target machine's sideloading policy; no admin rights needed to install, which matters on client machines; full access to the registry, service database and file system the product exists to manage; first-class winget installer type later.
- Cons: deviates from Dotify's documented MSIX standard; no sandboxing; no store-driven auto-update (mitigated by ADR-0004's update check); SmartScreen warning on every new release.

### MSIX, self-signed

- Pros: matches the Dotify standard as written; free.
- Cons: requires Developer Mode or `AllowAllTrustedApps` on every installing machine — a hard blocker on client sites; and the container's virtualized registry/file-system views would break most of the product.

### MSIX, paid certificate (e.g. Azure Trusted Signing)

- Pros: matches the standard; correctly attributed to Dotify.
- Cons: recurring cost, and the container problem above remains regardless of who signed the package.

### Machine-wide install (`{autopf}`, admin required)

- Pros: the install directory would not be user-writable, removing the tamper risk noted in ADR-0005.
- Cons: needs administrator rights at install time on machines where the technician may not have them, and blocks the "download and run it yourself" public use entirely.

## Consequences

Upkeep loses MSIX's sandboxing and update mechanism. It gains the ability to actually do its job on machines Dotify doesn't administer.

The install directory is user-writable, which is the accepted trade-off recorded in ADR-0005 and `SECURITY.md`: anyone who can already run code as that user can modify the binary that later requests elevation.

## Implementation Notes

- `scripts/build-installer.ps1` publishes `win-x64` self-contained with `-p:Version=$Version` (so the in-app update check has a real version to compare — see ADR-0004), runs `ISCC.exe`, and writes the `.sha256` sidecar. `@semantic-release/exec` invokes it once the next version is known.
- `PublishTrimmed` and `PublishReadyToRun` are both `False`: Microsoft's WinRT interop assemblies aren't trim-clean (and `TreatWarningsAsErrors` turns that into a build failure), and the ReadyToRun crossgen pack isn't in the lockfile, which fails CI's locked-mode restore.
- x64 only. ARM64 Windows runs the x64 build under emulation; a native ARM64 installer is a later decision, not a v1 one.
- Revisit before submitting a winget manifest, or if Dotify centralizes code-signing.

## References

- Pulsemap ADR-0002 (the same decision, same reasoning, for a tool that didn't also need HKLM access)
- ADR-0005 (runtime elevation — why installing without admin is compatible with doing admin work)
