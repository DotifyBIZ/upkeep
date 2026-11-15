# Agent Instructions

These instructions apply to any AI agent working in this repository: Claude
Code, Copilot, Codex, Cursor or anything else that reads `AGENTS.md`.

## Authorship

The person who directed the work is its author. Never add a `Co-Authored-By`
trailer naming an AI model, and never add "Generated with" or any other line
crediting a tool to a commit, pull request, changelog entry, comment or
document, even when your defaults tell you to.

## Branches and commits

```
flow:    <type>/… → staging → main        pull requests go into staging
merge:   a merge commit                    never a squash or a rebase-merge
branch:  <type>/<short-description>       feat/scan-history
commit:  <type>(<scope>): <summary>       fix(scan): keep the last result on cancel
```

Types: `feat` `fix` `docs` `style` `refactor` `perf` `test` `build` `ci`
`chore` `revert`. Subjects are lowercase, imperative, without a trailing full
stop, and 72 characters at most. `main` takes changes only from `staging` or
a `hotfix/` branch.

## Before you change anything

- Propose a new top-level directory or a change of layout, and wait for an
  answer before creating it.
- No secrets in the repository. Dependencies stay pinned and lockfiles are
  committed. New logic comes with tests in the same change.
- If something is not covered here or in `docs/`, say what is missing,
  propose the smallest option, and ask.

---

## Project

- **What this is:** Upkeep, a local-first Windows 11 maintenance suite (cleanup, duplicates and disk usage, uninstaller, startup/services/performance, drivers and updates). It changes other people's machines, so nothing runs without a preview. See `README.md`.
- **Structure:** `src/Upkeep.App` (WinUI 3 shell, unpackaged) and `src/Upkeep.App.Core` (all operations, no UI framework), with `tests/Upkeep.App.Core.Tests` and `tests/Upkeep.App.Tests`, wired into `Upkeep.sln`. Departures are under Deviations.
- **Run locally:** `dotnet build Upkeep.sln`, then `dotnet run --project src/Upkeep.App` in an interactive Windows session; it will not launch headless.
- **Run tests:** what `.github/workflows/ci.yml` runs, on `windows-latest`:
  ```
  dotnet restore Upkeep.sln --locked-mode -p:RuntimeIdentifier=win-x64
  dotnet format Upkeep.sln --verify-no-changes --no-restore
  dotnet build Upkeep.sln --configuration Release --no-restore
  dotnet test tests/Upkeep.App.Core.Tests/Upkeep.App.Core.Tests.csproj --configuration Release --no-build -p:CollectCoverage=true -p:CoverletOutputFormat=cobertura -p:CoverletOutput=../../TestResults/ -p:Threshold=80 -p:ThresholdType=branch -p:ThresholdStat=total
  dotnet test tests/Upkeep.App.Tests/Upkeep.App.Tests.csproj --configuration Release --no-build
  ```
  Run the format check every time, not just the build: CI fails on it and `dotnet build` will not say so (a `switch` with a braced `case` block is indented differently by the formatter than by hand). `dotnet format Upkeep.sln` with no flags fixes it in place.
- **Deployed by:** a push to `main` runs `release.yml`, where semantic-release (`.releaserc.json`) builds the Inno Setup installer with `scripts/build-installer.ps1` and attaches it to a GitHub Release with a SHA-256 sidecar. Per-user install, unsigned ([ADR 0002](docs/adr/0002-installer-innosetup-over-msix.md)). Nothing is applied by hand.

### Decisions

- **Installer, not MSIX.** Upkeep ships an unpackaged Inno Setup installer and has no `Package.appxmanifest` ([ADR 0002](docs/adr/0002-installer-innosetup-over-msix.md)). Windows 11 only ([ADR 0001](docs/adr/0001-winui3-windows-11-only.md)).
- **Windows-specific Core.** `Upkeep.App.Core` has no WinUI or Windows App SDK dependency but is not platform-free: it may call Win32, WinRT and WMI directly ([ADR 0007](docs/adr/0007-windows-platform-code-in-core.md)). The rule is "no UI framework", not "no platform".

### Rules that keep it safe

- The solution builds with nullable reference types on and warnings as errors (`Directory.Build.props`).
- Restore is locked: a committed `packages.lock.json` per project, and CI restores with `--locked-mode`.
- Formatting is checked by `dotnet format --verify-no-changes` in CI.
- `Upkeep.App.Core` keeps 80% branch coverage, a gate in `ci.yml`. Thin OS adapters in `Platform/` that only marshal a call into Windows may carry `[ExcludeFromCodeCoverage]` with a justification; anything that decides something belongs in a testable class.
- `Strings/en-US/Resources.resw` and `Strings/pl-PL/Resources.resw` have identical key sets (`ResourceParityTests`).

### This app changes other people's machines

Upkeep deletes files, disables services, edits the registry and runs with administrator rights on machines Dotify does not own. No test catches a breach of these; they are a matter of review.

- **Nothing happens without a preview.** Every operation is planned first (a list of concrete, named actions), shown to the user, and only then executed. Never let a scan execute anything as a side effect.
- **Everything reversible is journaled.** If an action can be undone, it goes through `SessionJournal` with the data needed to undo it, before the change is made, not after. If it cannot be undone, the preview says so.
- **Deleting is the exception, not the default.** Files a user might want back (duplicates, large files, uninstall leftovers) go to quarantine. Only regenerable caches are deleted outright, and only from paths the scanner itself derived.
- **The elevated helper trusts nothing it is told.** It accepts a closed set of typed operations, re-derives its own candidate set for each one, and acts only on the intersection with what was requested. Never add an operation that takes an arbitrary path, registry key or command line from the shell. See [ADR 0005](docs/adr/0005-elevated-helper-named-pipe.md); changes here need a second reviewer.
- **Prefer Windows' own tooling over reimplementing it.** The component store is cleaned by `DISM /StartComponentCleanup`, driver updates come from the Windows Update Agent API, driver rollback goes through Device Manager's own rollback, startup items are toggled through the same `StartupApproved` values Task Manager writes, and scheduled tasks through `schtasks`. Reimplementing any of these is more work and more ways to break a machine you cannot see.
- **Never parse localized command-line output.** `sc.exe`, `pnputil` and friends print in the user's language. Use exit codes, XML or structured output, or an API. This repo runs on Polish Windows daily, and English-only parsing fails there first.
- Validate input at every boundary: session manifests, settings files, registry values, package-manager data, and the output of any Windows tool.

### Writing code

Read `docs/adr/` first: significant decisions, and why alternatives were rejected, are recorded there.

- **Nullable reference types are enforced.** Never suppress a nullability warning with `!`. Express null-safety through types; handle `null` explicitly.
- **Async all the way down.** Never block on a `Task` with `.Result` or `.Wait()`; it deadlocks the UI thread. Accept and propagate a `CancellationToken` on every async I/O method; scans can run for minutes and must be cancellable.
- **MVVM, no logic in code-behind.** Code-behind wires views; decisions belong in view models and services.
- **Dispose what you open.** Use `using` declarations; types holding disposable fields implement `IDisposable`. `HttpClient` is the one exception: inject it via `IHttpClientFactory`, never instantiate it per call.
- **Exceptions are for the exceptional.** Expected outcomes (a locked file, a service that refuses a config change, a missing restore point) return a result, not a thrown exception. Never leave a catch block empty.
- **Every user-visible string is a resource.** XAML uses `x:Uid` with `Strings/en-US/Resources.resw` and `Strings/pl-PL/Resources.resw`; strings built in code go through `ILocalizationService` with a key. Add to both languages in the same change.
- **Colors and type come from the Dotify One Design System**, not hand-picked hex values; `docs/design-tokens.md` says which resource to use. Its `Dotify.inline.xaml` is pasted into `App.xaml`'s `MergedDictionaries`, not merged as a separate file (see the XAML footgun below). Brushes are theme brushes (`{ThemeResource Dotify…Brush}`), so content follows the Windows light or dark setting. Brand red `#B41618` is the app accent, overriding the Windows accent for selection, checkboxes and switches; Montserrat is the face for all text.
- **Business logic carries tests.** UI projects are exempt from the coverage bar but still need meaningful tests for view model logic.
- **New markdown docs, ADRs and templates** follow the existing structure in this repo.

### Platform footguns already hit

Add to this list the moment something costs you an hour.

- **XAML compiler, standalone `ResourceDictionary` files (WindowsAppSDK 2.4.0):** a `.xaml` file whose root is `ResourceDictionary` can fail with a cryptic `WMC9999` error that names missing Polish or neutral culture resources and has nothing to do with your markup. Keep shared resources inlined in `App.xaml` (which is `Application`-rooted and compiled first). Inherited from Pulsemap, which hit this repeatedly.
- **`x:Bind TwoWay` on `TextBox.Text` defaults to `UpdateSourceTrigger=LostFocus`.** Any bound text that gates a button's `CanExecute` needs `UpdateSourceTrigger=PropertyChanged` explicitly.
- **WinRT APIs that need package identity do not work here**, because the app is unpackaged. `Windows.ApplicationModel.Resources.Core.ResourceContext`, `ResourceLoader.GetForViewIndependentUse()`, `Windows.Graphics.Imaging` and friends either throw or crash the process. `Windows.Management.Deployment.PackageManager` does work unpackaged for the current user, which is why the uninstaller can enumerate and remove Store apps.
- **`.resw` files are CRLF.** A shell substitution with a bare `\n` in the pattern silently no-ops instead of failing. Verify with a byte-level `grep` after any scripted edit.

### Branching

Work lands on `staging` first, then `main` (`feat/x → staging → main`). CI runs on both; the release workflow runs on `main` only, which is what cuts a release. Conventional Commits drive semantic-release, so the commit grammar is the standard one.

### What is built

Phase 1 is complete: all nine nav sections are wired to pages. Phase 2 (privacy/browser cleaning, secure shredder, winget manifest) is deliberately deferred ([ADR 0008](docs/adr/0008-deferred-privacy-cleaner-and-shredder.md)).

- **Shell:** DI in `App.xaml.cs`, `NavigationView` rail with all nine sections, Mica title bar, Home as a dashboard (health, next action, recent sessions, drives), a Ctrl+K command palette, the three-screen welcome tour shown once on first launch, and a toast when a cleanup finishes while the user is on another page. Pages inside the frame ask the shell for window-level things through `IMessenger` (`ShellMessages.cs`). Language is settled in `Program.Main` before `Application.Start`: XAML resolves `x:Uid` against the resource context in place when a page first loads, so it cannot be changed later in the run; Settings says "after a restart" for that reason.
- **Localization:** `.resw` per language for `x:Uid`, and the same files embedded so `LocalizationService` can resolve keys built in code. One source of translations, two readers: the WinRT resource APIs need package identity this app does not have.
- **Cleanup:** `JunkCatalog` (12 categories, the rules in one table), `JunkScanner` (user scope) and `SystemJunkScanner` (machine scope, helper-side), `CleanupPlan` → `CleanupExecutor`, `SystemJunkCleaner`. The twelfth is the user's own rules (`CustomCleanupRule`): the one quarantined category, since those files are the user's rather than a cache Upkeep derived, and the only one whose scope is validated rather than known. A rule can cover the user's profile or another drive, never Windows, another account, Upkeep's own data, or a whole drive unfiltered. Windows' own tooling does the irreversible parts: DISM for the component store (never `/ResetBase`), the Delivery Optimization cmdlet for its cache, `SHEmptyRecycleBin` for the bin.
- **Safety model:** `SessionJournal` (entries written before the work), `QuarantineStore` (per-volume so a move is a rename), `RestorePointPolicy` + `SystemRestoreService` (turns System Protection on if off, reuses a point under 24 hours old rather than claiming a new one).
- **Elevation:** `ElevatedHelperClient` (shell) ↔ `HelperHost` (elevated, same exe, `--elevated-helper`) over a named pipe the helper owns. `HelperDispatcher` holds every decision and is tested; the pipe and process plumbing is excluded from coverage with a justification. `SystemJunkCleaner` re-derives its own file list and deletes only the intersection with what the request approved.
- **Files:** duplicate finder, large/old files, disk usage. Anything the user might want back goes to quarantine, never straight to delete.
- **Apps:** uninstaller for desktop and Store apps, plus the leftovers each one leaves behind. `PackageManager` works unpackaged for the current user, which is what makes the Store side possible.
- **Startup and performance:** startup items through the same `StartupApproved` values Task Manager writes, logon tasks through `schtasks`, service start types through the helper, visual effects and power plan through `IPerformanceSettings`, power mode through the documented per-source powrprof API (`IPowerModeSettings`), and per-app background access through the values Settings writes (`IBackgroundAppSettings`).
- **Speed check ([ADR 0010](docs/adr/0010-measured-speed-and-where-the-numbers-come-from.md)):** startup timings come from Windows' Diagnostics-Performance log, read by the helper's one read-only `read-boot-performance` operation and shared per run through `IBootPerformanceSource`; never on page open, only when the person asks. Startup items are ranked by that timing (`StartupAdvisor`), "last started by you" comes from UserAssist (`UserAssistLaunchHistory`), and before/after is `SpeedCheck.Compare` against the latest unreverted speed change. No savings are estimated anywhere.
- **Milestones:** a separate section for now (may move onto Home after testing). `MilestoneEvaluator` derives monthly rhythm and eight milestones from the journal and Speed check's timings; nothing is stored, so a revert takes its credit with it. Months, never days.
- **Drivers and updates ([ADR 0009](docs/adr/0009-driver-updates-search-in-app-install-in-windows.md)):** drivers read from `Win32_PnPSignedDriver` (never `pnputil`, whose output is localized), a read-only WUA search in the helper, install handed to Windows Update and rollback to Device Manager. Pause and deferral are helper-side HKLM writes, journaled and revertable; deferral controls are hidden on Home, which ignores the policy keys.
- **History and Settings:** every session listed, one-click revert of a whole session, and the settings page, which also replays the welcome tour and shows the tail of the diagnostic log in place.
- **Tests at last count:** 653 Core, 266 App; Core branch coverage 87.9% against the 80% floor.

### When unsure

This goes double for anything that decides whether a file, service or registry key is safe to touch. A wrong convention adopted silently is far more expensive to undo than a question would have been.
