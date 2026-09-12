# Upkeep Developer Guide

How the codebase is organized, how a scan becomes an executed change, and where the rules live that
keep a maintenance tool from doing damage. Process — branching, commits, the PR checklist — is in
[CONTRIBUTING.md](../CONTRIBUTING.md); the engineering rules and platform footguns are in
[CLAUDE.md](../CLAUDE.md), which is worth reading before your first change.

## Contents

- [Prerequisites](#prerequisites)
- [Getting the code running](#getting-the-code-running)
- [Solution structure](#solution-structure)
- [The layering rule](#the-layering-rule)
- [How work happens: scan, plan, execute](#how-work-happens-scan-plan-execute)
- [The safety model in code](#the-safety-model-in-code)
- [The elevated helper](#the-elevated-helper)
- [Localization](#localization)
- [Dependency injection](#dependency-injection)
- [Testing strategy](#testing-strategy)
- [Adding things — a cookbook](#adding-things--a-cookbook)
- [CI/CD](#cicd)

## Prerequisites

- Windows 11 (build 22000 or later) — see [ADR-0001](adr/0001-winui3-windows-11-only.md).
- .NET 9 SDK (pinned in `global.json`).
- An interactive desktop session. The app is WinUI 3; it will not launch headless.

## Getting the code running

```powershell
git clone https://github.com/DotifyBIZ/upkeep.git
cd upkeep
dotnet build Upkeep.sln
dotnet run --project src/Upkeep.App
```

Before pushing, run what CI runs — **including the format check**, which `dotnet build` will not
tell you about:

```powershell
dotnet format Upkeep.sln --verify-no-changes
dotnet build Upkeep.sln --configuration Release
dotnet test Upkeep.sln --configuration Release
```

`dotnet format Upkeep.sln` (no flags) fixes formatting in place.

## Solution structure

```
src/Upkeep.App.Core/        Every operation, plus the safety model. No WinUI.
  Abstractions/             Interfaces the shell implements (localization, pickers, update check)
  Apps/                     Installed apps, uninstalling, leftovers
  Cleanup/                  Junk categories, scanners, plan, executor
  Elevation/                Helper protocol, dispatcher, pipe client and host
  Files/                    Duplicates, large files, disk usage, file actions
  Formatting/               Byte sizes and other display formatting
  Logging/                  The local diagnostic log
  Performance/              Visual effects, power plans
  Platform/                 Thin Windows adapters (registry, recycle bin, processes, tools)
  Quarantine/               The 7-day quarantine store
  Safety/                   Restore points and their policy
  Services/                 Service tiering and scanning
  Sessions/                 The session journal — the undo log
  Settings/                 App preferences
  Startup/                  Startup items and toggling
  Storage/                  Drive space

src/Upkeep.App/             The WinUI 3 shell
  Converters/               Value converters (kept out of view models on purpose)
  Services/                 Shell-side implementations of Core abstractions
  Strings/{en-US,pl-PL}/    Resources.resw — one source, two readers (see Localization)
  ViewModels/               One view model per page, plus display records
  Views/                    Pages and their code-behind

tests/Upkeep.App.Core.Tests/   The bar that matters: 80% branch coverage, enforced in CI
tests/Upkeep.App.Tests/        View model and localization tests
```

## The layering rule

`Upkeep.App.Core` has **no WinUI or Windows App SDK reference** — but it is Windows-specific and
calls Win32, WinRT and WMI directly. The rule is "no UI framework", not "no platform"; see
[ADR-0007](adr/0007-windows-platform-code-in-core.md) for why this deviates from Pulsemap.

Inside Core, the split is by testability:

- `Platform/` holds thin adapters that only marshal a call into Windows. These may carry
  `[ExcludeFromCodeCoverage]` **with a justification**.
- Everything that *decides* something — which files a category includes, whether a service is safe
  to disable, whether a folder looks like an app's leftovers — is a plain class with no I/O, and is
  covered by the coverage gate.

Review rule: an excluded class may contain no branch that decides *what* to touch. Only the call,
its arguments, and mapping the result.

Capabilities that genuinely need a window (file and folder pickers, launching Settings deep links)
stay behind an interface in `Core/Abstractions/`, implemented in `Upkeep.App`.

## How work happens: scan, plan, execute

Every area follows the same three steps, and they are separate types on purpose:

1. **Scan** — read-only, cancellable, returns evidence. `JunkScanner`, `DuplicateFinder`,
   `InstalledAppScanner`, `StartupItemScanner`. A scan never changes anything; that is a contract,
   not a convention.
2. **Plan** — the user's selection turned into concrete work. `CleanupPlan` is the clearest example:
   it is built from scans plus the ticked categories and never re-reads the disk, so what executes
   is exactly what was previewed.
3. **Execute** — `CleanupExecutor`, `FileActionExecutor`, `LeftoverRemover`, `StartupItemToggler`.
   Each journals before it acts, reports what actually happened, and treats a locked file or a
   refused change as a number on the results screen rather than an aborted run.

## The safety model in code

[ADR-0006](adr/0006-safety-model.md) has the reasoning; this is where it lives:

| Mechanism | Type | Note |
|---|---|---|
| Preview | `CleanupPlan`, scan results | Nothing executes without a confirmed plan |
| Quarantine | `QuarantineStore` | Per-volume, so a move is a rename, not a copy |
| Restore point | `RestorePointPolicy`, `SystemRestoreService` | Turns System Protection on if off; reuses a <24h point rather than claiming a new one |
| Undo log | `SessionJournal`, `SessionEntry` | Written **before** each action, not after |

Two rules worth internalizing:

- **Journal first.** A session interrupted by a crash or a closed lid must still say what it had
  started. `AppendAsync` writes the entry, the work runs, then `UpdateEntryAsync` marks it completed
  or failed.
- **Quarantine is not "freed".** `FileActionOutcome.FreedBytes` deliberately reports zero for
  quarantined files — the space comes back when the quarantine is purged, and saying otherwise is
  the "7.7 GB freed" that isn't.

## The elevated helper

The shell runs as the signed-in user. Anything machine-wide goes to a helper process — the same
executable, re-launched with `--elevated-helper`, which skips WinUI entirely
([ADR-0005](adr/0005-elevated-helper-named-pipe.md)).

- `ElevatedHelperClient` (shell) ↔ `HelperHost` (elevated) over a named pipe the **helper** owns.
- `HelperDispatcher` turns one request into one response. It is a `switch`, on purpose: what this
  process is willing to do should be readable top to bottom in one review.
- `IHelperOperations` is the seam that makes the dispatcher testable without administrator rights.

Two rules that are not negotiable:

1. **The helper trusts nothing it is told.** A request names a *kind of work and a scope*, never a
   path to act on blindly. `SystemJunkCleaner` re-derives the category's own file list and removes
   only the intersection with what the request approved.
2. **No ambient per-user state in the helper.** Under over-the-shoulder elevation it runs as a
   different account, so `%TEMP%` and `HKEY_CURRENT_USER` there belong to the technician, not the
   user. The shell's owner SID is resolved from the parent process, and per-user work stays in the
   shell.

## Localization

Two readers, one source:

1. **XAML** uses `x:Uid` against `Strings/{lang}/Resources.resw` — the normal WinUI mechanism.
2. **Strings built in code** go through `ILocalizationService`, which parses the *same* `.resw`
   files, embedded as resources. Every WinRT resource-lookup API needs package identity this
   unpackaged app doesn't have, so parsing the same source keeps one set of translations rather than
   a second hand-maintained table.

Core never holds user-facing text: a scanner returns a **key** and the shell turns it into words.
That is what lets the same operation serve a UI, a log line and a test.

The language is settled in `Program.Main` *before* `Application.Start`, because XAML resolves
`x:Uid` against the resource context in place when a page first loads. That is also why Settings
says a language change applies after a restart.

`ResourceParityTests` fails the build if the two languages' key sets drift, if a value is empty, or
if a translation's `{0}` placeholders don't match English.

## Dependency injection

`App.xaml.cs`'s `ConfigureServices` is the single composition root. Core services and platform
adapters are singletons (they are stateless); view models are transient, one per navigation.

## Testing strategy

- `Upkeep.App.Core.Tests` carries the 80% branch-coverage floor, enforced in CI. Fakes live in
  `Fakes/` and are hand-rolled — the interfaces are small, and a mocking library would be a
  dependency for no gain.
- `FakeWellKnownPaths` builds a throwaway directory tree and points every well-known location at it.
  **Use it.** A cleaning tool whose tests run against the real `%TEMP%` is a bad idea exactly once.
- `FakeRegistryProbe` is an in-memory registry. The real `IRegistryProbe` only ever writes to the
  current user's hive, so no test can reach HKLM even by accident.
- `Upkeep.App.Tests` covers view models and localization. Keep WinUI types out of view models:
  a computed `Brush` property looks tidy until a plain unit test constructs it with no
  `Application.Current` and the class stops being testable. That is what `Converters/` is for.

## Adding things — a cookbook

**A new junk category:** add it to `JunkCategoryId`, give it a row in `JunkCatalog` (scope, removal
kind, whether it's on by default), add `JunkCategory{Id}Name`/`Description` to both `.resw` files,
and implement it in `JunkScanner` (user scope) or `SystemJunkScanner` (machine-wide). The catalog
test will fail until the category is complete.

**A new elevated operation:** add a `HelperRequest` derived type with a discriminator, a matching
response, a case in `HelperDispatcher`, and a method on `IHelperOperations`. Re-derive whatever you
are about to touch inside the helper. Changes here need a second reviewer.

**A new page:** view model in `ViewModels/`, page in `Views/`, register both in `ConfigureServices`,
add the nav mapping in `MainPage.xaml.cs` (two places: `PageFor` and the `Navigated` handler), and
add every string to both `.resw` files.

**A new session entry type:** add it to `SessionEntry`'s `[JsonDerivedType]` list with a **stable**
discriminator — those strings are on users' disks, and renaming one orphans the history of every
past session.

## Platform footguns

`CLAUDE.md` keeps the authoritative, continuously-updated list. Know that it exists and read it
before you spend an hour on one of them: the `WMC9999` XAML error that has nothing to do with your
markup, `x:Bind TwoWay` on `TextBox.Text` defaulting to `LostFocus`, WinRT APIs that need package
identity, and `.resw` files being CRLF.

## CI/CD

- `.github/workflows/ci.yml` runs format, build, both test projects and the coverage gate on
  `windows-latest`, for pushes and pull requests to `main` and `staging`.
- `.github/workflows/release.yml` runs semantic-release on `main` only: it builds the InnoSetup
  installer via `scripts/build-installer.ps1` and attaches it with a SHA-256 sidecar.
- Work lands on `staging` first, then `main`. Landing on `main` is what cuts a release.
- Dependencies are pinned with committed `packages.lock.json` files, and CI restores in locked mode.
  The app project declares `RuntimeIdentifiers` so that restore records the win-x64 assets a
  self-contained Windows App SDK build needs — without it CI fails with `NETSDK1112` while a local
  build survives on a warm NuGet cache.

## Where to ask questions

Open an issue and tag it `question`.
