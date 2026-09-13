# Architecture Decision Records

This directory records significant architectural decisions for Upkeep — the "why," not the mechanics already visible in the code. Use [template.md](template.md) for new ADRs.

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-winui3-windows-11-only.md) | WinUI 3 as the shell, Windows 11 only | Accepted |
| [0002](0002-installer-innosetup-over-msix.md) | InnoSetup installer instead of MSIX | Accepted |
| [0003](0003-local-diagnostic-logging.md) | Local diagnostic logging, not telemetry | Accepted |
| [0004](0004-update-check-network-call.md) | The in-app update check, and the other traffic Upkeep causes | Accepted |
| [0005](0005-elevated-helper-named-pipe.md) | One elevated helper per session, over a named pipe | Accepted |
| [0006](0006-safety-model.md) | The safety model — preview, tiered removal, restore point, session journal | Accepted |
| [0007](0007-windows-platform-code-in-core.md) | Windows platform code lives in Core, not behind interfaces in the shell | Accepted |
| [0008](0008-deferred-privacy-cleaner-and-shredder.md) | Privacy cleaner and secure file shredder — considered, deferred | Accepted |
| [0009](0009-driver-updates-search-in-app-install-in-windows.md) | Search for driver updates in app, install them in Windows | Accepted |
