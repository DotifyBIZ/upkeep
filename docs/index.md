# Upkeep

Upkeep is a local-first maintenance suite for Windows 11. It cleans junk, finds duplicate and large
files, uninstalls apps with their leftovers, manages startup items and performance settings, and
shows driver and Windows Update status. It has no account, no server and no telemetry. Every change
is previewed first and can be undone from History.

It is built by Dotify and runs on the person's own PC, per user, from an unsigned installer.
Source and releases are on [GitHub](https://github.com/DotifyBIZ/upkeep).

## Where it runs

- Windows 11 (build 22000 or later), 64-bit. See [ADR 0001](adr/0001-winui3-windows-11-only.md).
- Installed per user under `%LocalAppData%\Programs\Upkeep`. No administrator rights to install.
- Administrator rights are asked for at run time, once per session, only for actions that need them.

## Read by audience

- **Using the app:** [User guide](user-guide.md).
- **Installing and supporting it:** [Admin guide](admin-guide.md).
- **Owning it:** [Ownership](ownership.md).
- **Changing the code:** [Developer guide](developer-guide.md), [Design tokens](design-tokens.md).
- **Why it is built this way:** [Decisions](adr/README.md).
