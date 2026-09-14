# Upkeep — Instructions for AI Agents

Upkeep is developed by Dotify. It follows Dotify's internal engineering standards, restated below in project-specific terms — this file is self-contained; it doesn't link back to Dotify's internal engineering-system host, since this repo is public and that host isn't.

## Two rules that override your defaults

**1. You are a tool, not an author.** Never add `Co-Authored-By` trailers naming an AI model, "Generated with..." tags, or any agent attribution to commits, PR descriptions, changelog entries, or code comments. The person who directed the work is the author and is accountable for it — this applies to every contributor, not just Dotify staff.

**2. Never scaffold a new top-level directory or restructure existing ones without asking first.** Propose the actual tree and wait for confirmation. If a human changes what you proposed, record why in `docs/adr/`.

## This app changes other people's machines

Upkeep deletes files, disables services, edits the registry and runs with administrator rights on machines Dotify does not own. Treat every one of these as non-negotiable:

- **Nothing happens without a preview.** Every operation is planned first (a list of concrete, named actions), shown to the user, and only then executed. Never let a scan execute anything as a side effect.
- **Everything reversible is journaled.** If an action can be undone, it goes through `SessionJournal` with the data needed to undo it, before the change is made — not after. If it cannot be undone, it says so in the preview.
- **Deleting is the exception, not the default.** Files a user might want back (duplicates, large files, uninstall leftovers) go to quarantine. Only regenerable caches are deleted outright, and only from paths the scanner itself derived.
- **The elevated helper trusts nothing it is told.** It accepts a closed set of typed operations, re-derives its own candidate set for each one, and acts only on the intersection with what was requested. Never add an operation that takes an arbitrary path, registry key or command line from the shell. See `docs/adr/0005-elevated-helper-named-pipe.md`; changes here need a second reviewer.
- **Prefer Windows' own tooling over reimplementing it.** The component store is cleaned by `DISM /StartComponentCleanup`, driver updates come from the Windows Update Agent API, driver rollback goes through Device Manager's own rollback, startup items are toggled through the same `StartupApproved` values Task Manager writes, and scheduled tasks through `schtasks`. Reimplementing any of these is both more work and more ways to break a machine you can't see.
- **Never parse localized command-line output.** `sc.exe`, `pnputil` and friends print in the user's language. Use exit codes, XML/structured output, or an API — this repo runs on Polish Windows daily, and English-only parsing fails there first.

## Before writing code

- **Read `docs/adr/` first.** Significant decisions — and why alternatives were rejected — are recorded there, not just in commit history.
- **Nullable reference types are enforced.** Never suppress a nullability warning with `!`. Express null-safety through types; handle `null` explicitly.
- **Async all the way down.** Never block on a `Task` with `.Result` or `.Wait()` — this deadlocks the UI thread. Accept and propagate a `CancellationToken` on every async I/O method; scans can run for minutes and must be cancellable.
- **`Upkeep.App.Core` has zero WinUI/Windows App SDK dependency** — but it *is* Windows-specific and may call Win32, WinRT and WMI directly (`docs/adr/0007-windows-platform-code-in-core.md`). The rule is "no UI framework", not "no platform".
- **MVVM, no logic in code-behind.** Code-behind wires views; decisions belong in view models and services.
- **Dispose what you open.** Use `using` declarations; types holding disposable fields implement `IDisposable`. `HttpClient` is the one exception — inject via `IHttpClientFactory`, never instantiate per call.
- **Exceptions are for the exceptional.** Expected outcomes (a locked file, a service that refuses a config change, a missing restore point) return a result, not a thrown exception. Never leave a catch block empty.
- **Every user-visible string is a resource.** XAML uses `x:Uid` + `Strings/en-US/Resources.resw` and `Strings/pl-PL/Resources.resw`; strings built in code go through `ILocalizationService` with a key. Add to **both** languages in the same change — a test asserts the two files have identical key sets.
- **Colors and type come from the design tokens**, not hand-picked hex values — see `docs/design-tokens.md`. They live inlined in `App.xaml`'s `ResourceDictionary`, not a separate merged file (see the XAML note below).

## Platform footguns already hit

<!-- Add to this list the moment something costs you an hour. It's the reason this file exists. -->

- **XAML compiler, standalone `ResourceDictionary` files (WindowsAppSDK 2.4.0):** a `.xaml` file whose root is `ResourceDictionary` can fail with a cryptic `WMC9999` error that names missing Polish/neutral culture resources and has nothing to do with your markup. Keep shared resources inlined in `App.xaml` (which is `Application`-rooted and compiled first). Inherited from Pulsemap, which hit this repeatedly.
- **`x:Bind TwoWay` on `TextBox.Text` defaults to `UpdateSourceTrigger=LostFocus`.** Any bound text that gates a button's `CanExecute` needs `UpdateSourceTrigger=PropertyChanged` explicitly.
- **WinRT APIs that need package identity don't work here** — this app is unpackaged. `Windows.ApplicationModel.Resources.Core.ResourceContext`, `ResourceLoader.GetForViewIndependentUse()`, `Windows.Graphics.Imaging` and friends either throw or crash the process. `Windows.Management.Deployment.PackageManager` *does* work unpackaged for the current user, which is why the uninstaller can enumerate and remove Store apps.
- **`.resw` files are CRLF.** A shell substitution with a bare `\n` in the pattern silently no-ops instead of failing. Verify with a byte-level `grep` after any scripted edit.

## Non-negotiable, every change

- No secrets in code or version control — ever, including test fixtures.
- Validate input at every boundary: session manifests, settings files, registry values, package-manager data, and the output of any Windows tool.
- Business logic (`Upkeep.App.Core`) carries tests — minimum 80% branch coverage, enforced in CI. UI projects are exempt from that bar but still need meaningful tests for view model logic.
- Thin OS adapters in Core (`Platform/`) that only marshal a call into Windows may carry `[ExcludeFromCodeCoverage]` with a justification. Everything that *decides* something — what to include, what to skip, what to call safe — belongs in a testable class instead, and is covered.
- Dependencies are pinned, with a committed lockfile.
- New markdown docs, ADRs, and templates follow the existing structure in this repo — don't invent a new documentation convention.

## When unsure, stop and ask

Don't invent a convention to fill a gap. Name what's missing, propose the smallest reasonable option, and ask. A wrong convention adopted silently is far more expensive to undo than a question would have been. This goes double for anything that decides whether a file, service or registry key is safe to touch.

---

## Project

- **What this is:** Upkeep — a local-first Windows 11 maintenance suite (cleanup, duplicates/disk usage, uninstaller, startup/services/performance, drivers and updates). See `README.md`.
- **Structure:** `src/Upkeep.App` (WinUI 3 shell, unpackaged) + `src/Upkeep.App.Core` (all operations, no UI dependency) + `tests/Upkeep.App.Core.Tests` + `tests/Upkeep.App.Tests`, wired into `Upkeep.sln`.
- **Run locally:** `dotnet build Upkeep.sln`, then `dotnet run --project src/Upkeep.App` on a machine with a display (needs an interactive Windows session — it won't launch headless).
- **Run tests:** `dotnet test Upkeep.sln`.
- **Verify before committing:** `dotnet format Upkeep.sln --verify-no-changes`, `dotnet build Upkeep.sln --configuration Release`, `dotnet test Upkeep.sln --configuration Release`. **Run the format check every time, not just the build** — CI fails on it, and `dotnet build` will not tell you: a `switch` with a braced `case` block is indented differently by the formatter than by hand, and that alone is enough to fail a green build. `dotnet format Upkeep.sln` (no flags) fixes it in place.
- **Branching:** work lands on `staging` first, then `main` (`feat/x → staging → main`). CI runs on both; the release workflow runs on `main` only, which is what cuts a release.
- **Deployed by:** GitHub Releases — semantic-release (`.releaserc.json`) on push to `main` builds the InnoSetup installer via `scripts/build-installer.ps1` and attaches it with a SHA-256 sidecar. Per-user install, unsigned (ADR-0002).
- **Design:** brand red `#B41618` is the app accent, overriding the Windows accent for selection, checkboxes and switches; Montserrat is the Dotify brand face, used for headings and the wordmark, with Segoe UI Variable for everything else. `docs/design-tokens.md` is the source of truth.
- **Built so far:**
  - **Shell:** DI in `App.xaml.cs`, `NavigationView` rail with all eight sections, Mica title bar, Home (greeting, drives, update banner). Language is settled in `Program.Main` *before* `Application.Start` — XAML resolves `x:Uid` against the resource context in place when a page first loads, so it can't be changed later in the run; Settings says "after a restart" for that reason.
  - **Localization:** `.resw` per language for `x:Uid`, and the same files embedded so `LocalizationService` can resolve keys built in code. One source of translations, two readers — the WinRT resource APIs need package identity this app doesn't have. `ResourceParityTests` fails the build if the two languages drift.
  - **Cleanup (complete):** `JunkCatalog` (11 categories, the rules in one table), `JunkScanner` (user scope) and `SystemJunkScanner` (machine scope, helper-side), `CleanupPlan` → `CleanupExecutor`, `SystemJunkCleaner`. Windows' own tooling does the irreversible parts: DISM for the component store (never `/ResetBase`), the Delivery Optimization cmdlet for its cache, `SHEmptyRecycleBin` for the bin.
  - **Safety model:** `SessionJournal` (entries written *before* the work), `QuarantineStore` (per-volume so a move is a rename), `RestorePointPolicy` + `SystemRestoreService` (turns System Protection on if off, reuses a <24h point rather than claiming a new one).
  - **Elevation:** `ElevatedHelperClient` (shell) ↔ `HelperHost` (elevated, same exe, `--elevated-helper`) over a named pipe the helper owns. `HelperDispatcher` holds every decision and is tested; the pipe and process plumbing is excluded from coverage with a justification. `SystemJunkCleaner` re-derives its own file list and deletes only the intersection with what the request approved.
  - **Files:** duplicate finder, large/old files, disk usage. Anything the user might want back goes to quarantine, never straight to delete.
  - **Apps:** uninstaller for desktop and Store apps, plus the leftovers each one leaves behind. `PackageManager` works unpackaged for the current user, which is what makes the Store side possible.
  - **Startup & performance:** startup items through the same `StartupApproved` values Task Manager writes, logon tasks through `schtasks`, service start types through the helper, visual effects and power plan through `IPerformanceSettings`.
  - **Drivers & updates:** drivers read from `Win32_PnPSignedDriver` (never `pnputil` — localized output), a read-only WUA search in the helper, install handed to Windows Update and rollback to Device Manager (ADR-0009). Pause and deferral are helper-side HKLM writes, journaled and revertable; deferral controls are hidden on Home, which ignores the policy keys.
  - **History & Settings:** every session listed, one-click revert of a whole session, and the settings page.
  - **Tests:** 525 Core, 172 App; Core branch coverage 86.5% against the 80% floor.
- **Not built yet:** nothing in Phase 1 — all eight nav sections are wired to pages. Phase 2 (privacy/browser cleaning, secure shredder, winget manifest) is deliberately deferred (ADR-0008).
