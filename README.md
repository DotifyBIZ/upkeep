<div align="center">
  <img src=".github/images/logo.png" alt="Upkeep" width="120" height="120">

  # Upkeep

  **Find what's slowing it down before your clients do.**

  Clean it, tune it, keep it — Windows upkeep done right, entirely on your own machine. No account, no server, no telemetry.

  [![CI](https://github.com/DotifyBIZ/upkeep/actions/workflows/ci.yml/badge.svg)](https://github.com/DotifyBIZ/upkeep/actions/workflows/ci.yml)
  [![License: PolyForm Shield 1.0.0](https://img.shields.io/badge/license-PolyForm%20Shield%201.0.0-blue)](LICENSE)
  ![Platform: Windows 11](https://img.shields.io/badge/platform-Windows%2011-0078D6)
  ![.NET 9](https://img.shields.io/badge/.NET-9-512BD4)

</div>

## Contents

- [Upkeep](#upkeep)
  - [Contents](#contents)
  - [What it does](#what-it-does)
  - [What it deliberately doesn't do](#what-it-deliberately-doesnt-do)
  - [How it stays safe](#how-it-stays-safe)
  - [Status](#status)
  - [Installing](#installing)
  - [Building from source](#building-from-source)
  - [Architecture](#architecture)
  - [Roadmap](#roadmap)
  - [Contributing](#contributing)
  - [License](#license)

## What it does

Upkeep is a local-first Windows maintenance suite: the whole health of a PC in one place, rather than a cleaner bolted onto a disk-space meter. It's built to be safe enough for anyone to run on their own machine, and deep enough for a technician to take further on purpose.

- **Junk and cache cleanup** — user and system temp files, browser caches per profile (Edge/Chrome/Firefox), Recycle Bin, thumbnail and DirectX shader caches, crash dumps and error reports, Delivery Optimization, and Windows Update cleanup through Windows' own DISM
- **Duplicate and large-file finder** — duplicates matched by content hash, never by name, so a delete action is never based on a guess; large and forgotten files surfaced by size and age thresholds you set
- **Disk usage visualizer** — a native treemap of where the space actually went, drillable folder by folder
- **Uninstaller with leftover cleanup** — desktop and Store apps in one list, uninstalled with their own uninstaller, then a preview of the folders and registry keys *that app* left behind
- **Startup manager and performance tuning** — Run keys, Startup folder, logon tasks and background apps, toggled the way Task Manager does it; services labelled honestly from "commonly safe" to locked; visual effects, power plan and search indexing
- **Driver and Windows Update management** — installed driver versions from Device Manager, available driver updates from Windows Update itself, feature-update pause and deferral through Windows' own settings, and driver rollback where Windows kept a previous driver
- **English and Polish**, both shipped from day one, following your Windows display language unless you pick otherwise

## What it deliberately doesn't do

- **No malware or threat scanning.** Windows Defender owns that. Upkeep stays out of the AV space entirely — partly scope discipline, partly so a cleaning tool doesn't end up being flagged by other AV engines itself.
- **No built-in driver or hardware database.** Driver update availability comes from Windows Update and Device Manager, not a private catalogue Dotify would have to maintain.
- **No general registry "cleaner."** Registry cleanup is scoped to remnants of an app you just uninstalled — never a broad scan-and-fix pass over the whole registry. That category of tool is mostly placebo, and unscoped registry edits are exactly what erodes trust in a maintenance tool.
- **No privacy/browser-data cleaner and no secure file shredder** — considered and deliberately deferred, not forgotten; see [ADR-0008](docs/adr/0008-deferred-privacy-cleaner-and-shredder.md).
- **No telemetry, no account, no server.** Everything runs against the local machine; diagnostics are a local log file you choose whether to share ([ADR-0003](docs/adr/0003-local-diagnostic-logging.md)). Two things touch the network, neither carrying anything about you or this PC: an optional, on-by-default check of GitHub's public release list on launch, with a Settings toggle ([ADR-0004](docs/adr/0004-update-check-network-call.md)), and — only while you're actually using the Drivers page — Windows Update's own check for driver updates.

## How it stays safe

These compose; they aren't alternatives.

1. **Dry-run preview.** Every scan shows exactly what it would touch, down to the file, and you confirm before anything runs.
2. **Non-destructive by default.** Files you'd want back — duplicates, large files, uninstall leftovers — move to Upkeep's quarantine for 7 days, not oblivion. Caches and temp files, which Windows and your apps rebuild on their own, are deleted, and anything irreversible says so in the preview.
3. **A System Restore point** before any run that includes a destructive system-level action.
4. **An undo log.** Every session is written to a local manifest, and a session can be reverted in one click — startup items, services, registry keys, quarantined files and settings all go back.

## Status

Phase 1 is complete. Everything described above ships in v1 — all five areas, the full safety model, and both languages. [CHANGELOG.md](CHANGELOG.md), generated from commit history, is what actually changed in each release.

The installer is unsigned and installs per-user, so expect a SmartScreen warning the first time you run it; see [Installing](#installing).

Backed by a real automated test suite, with branch coverage on the platform-independent engine enforced at an 80% floor in CI.

## Installing

Download the latest `UpkeepSetup-<version>.exe` from [Releases](https://github.com/DotifyBIZ/upkeep/releases) and run it — no admin rights needed, installs per-user. Windows SmartScreen will likely warn you since the installer isn't code-signed yet; see the [User Guide](docs/user-guide.md#faq) for why that's expected. Full instructions, and how to use the app once it's installed, are in the **[User Guide](docs/user-guide.md)**.

Windows 11 (build 22000) or later, 64-bit.

## Building from source

Requires the .NET 9 SDK and Windows 11.

```powershell
git clone https://github.com/DotifyBIZ/upkeep.git
cd upkeep
dotnet build Upkeep.sln
dotnet run --project src/Upkeep.App
```

`dotnet test Upkeep.sln` runs the full suite. Upkeep needs an interactive Windows session — it won't launch headless.

## Architecture

- **`Upkeep.App.Core`** — every scan, clean and analysis operation, the safety model (quarantine, session journal, restore points) and the elevation protocol. No WinUI dependency, so the same code serves the UI, the elevated helper and the tests.
- **`Upkeep.App`** — the WinUI 3 shell: native Fluent design, Mica backdrop, unpackaged (no MSIX — see [ADR-0002](docs/adr/0002-installer-innosetup-over-msix.md)) so it runs on client machines Dotify doesn't administer.
- **Elevation** is a single helper process per session: the first action needing admin rights raises one UAC prompt, and the shell talks to that helper over a local named pipe for the rest of the session. The helper accepts a closed set of typed operations and re-derives what it is about to touch rather than trusting paths from the shell — see [ADR-0005](docs/adr/0005-elevated-helper-named-pipe.md).
- **Windows' own tooling comes first**: DISM for the component store, Windows Update's own API for driver updates, Device Manager's rollback, and Task Manager's StartupApproved mechanism for startup items. Less to maintain, and far less to get dangerously wrong.

Architectural decisions, and the alternatives rejected, are recorded in [`docs/adr/`](docs/adr/). For how the codebase is organized, see the **[Developer Guide](docs/developer-guide.md)**.

## Roadmap

- **Phase 1 (MVP):** all five areas above — junk and cache cleanup, duplicate/large-file finder and disk usage visualizer, uninstaller with leftover cleanup, startup manager and performance tuning, driver and Windows Update management — plus the full safety model (preview, quarantine, restore point, undo log), in English and Polish.
- **Phase 2:** privacy and browser-data cleaning, a secure file shredder, and a winget manifest. Considered for v1 and deliberately left out ([ADR-0008](docs/adr/0008-deferred-privacy-cleaner-and-shredder.md)).

## Contributing

Contributions are welcome from day one — see [CONTRIBUTING.md](CONTRIBUTING.md) for the process, and the **[Developer Guide](docs/developer-guide.md)** for how the codebase is organized. Agents and contributors working in this repo should read [CLAUDE.md](CLAUDE.md) first; it documents the engineering standards and the platform footguns this project has already hit, so they don't get hit twice.

## License

[PolyForm Shield 1.0.0](LICENSE) — free to use, modify, and redistribute for any purpose except building a competing product or service on top of it.

This project follows a [Code of Conduct](CODE_OF_CONDUCT.md).

---

<div align="center">
  <sub>Crafted with ❤️ by <a href="https://dotify.biz">Dotify</a></sub>
</div>
