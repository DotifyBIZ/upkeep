# Ownership

Owner in the catalog: TR-ITOperations. Lifecycle: production.

## What the team is accountable for

- Releases. A push to `main` runs the release workflow. semantic-release builds the Inno Setup
  installer and attaches it, with a SHA-256 sidecar, to a GitHub Release.
- The safety rules in [AGENTS.md](https://github.com/DotifyBIZ/upkeep/blob/main/AGENTS.md). The app
  deletes files, disables services and edits the registry on machines Dotify does not own.
- Changes to the elevated helper. They need a second reviewer.
- Security reports sent to cert@dotify.biz or through GitHub's private vulnerability reporting.

## Depends on

| Dependency | Why |
|---|---|
| `one-design-system` (component) | The XAML resources in `App.xaml` are pasted from the Dotify One Design System. |
| `github-rest-api` (API) | The update check reads the public latest-release endpoint. |
| Windows 11 itself | DISM, the Windows Update Agent API, Device Manager rollback, `schtasks`, StartupApproved values. |

## Depended on by

Nothing in the catalog. Upkeep serves no API.

## Data

Upkeep runs on the person's PC and holds nothing centrally. On each machine it keeps settings, a
local log, a session journal and quarantined files, under `%LocalAppData%\Upkeep`. The quarantine
can hold personal files the user chose to move. Nothing leaves the machine.

## Recurring duties

- **Dependency updates:** Dependabot opens weekly pull requests against `staging` for GitHub
  Actions, NuGet and npm
  ([dependabot.yml](https://github.com/DotifyBIZ/upkeep/blob/main/.github/dependabot.yml)).
- **Lockfiles:** a committed `packages.lock.json` per project; CI restores in locked mode.
- **Code signing:** the installer is unsigned today. Signing is not set up.

## Lifecycle

The product is licensed under
[PolyForm Shield 1.0.0](https://github.com/DotifyBIZ/upkeep/blob/main/LICENSE). Phase 1 is complete.
Phase 2 (privacy cleaner, file shredder, winget manifest) is deferred on purpose
([ADR 0008](adr/0008-deferred-privacy-cleaner-and-shredder.md)). How a retirement would be decided
is not written down yet.
