# Admin guide

For people who install Upkeep on PCs and support it. There is no server to run.

## Requirements

- Windows 11, build 22000 or later, 64-bit.
- An interactive desktop session. The app does not start headless.

## Install

- Get `UpkeepSetup-<version>.exe` from the
  [Releases page](https://github.com/DotifyBIZ/upkeep/releases). Each release has a SHA-256 sidecar file.
- The installer is per user, needs no administrator rights, and installs to
  `%LocalAppData%\Programs\Upkeep`. It offers English and Polish. A desktop icon is optional.
- The installer is unsigned. SmartScreen warns on first run
  ([ADR 0002](adr/0002-installer-innosetup-over-msix.md)).
- Upgrade by running a newer installer. It recognises the same app.
- Uninstall from Windows Settings, Installed apps. The installer script removes only the program
  folder. Delete `%LocalAppData%\Upkeep` by hand if you want the data gone.

## Elevation

Upkeep runs as the signed-in user. When an action needs administrator rights, it starts one
elevated helper (the same executable, with `--elevated-helper`). Windows shows one UAC prompt per
session. The helper talks to the app over a named pipe it owns. It accepts only a closed set of
typed operations and re-derives what it will touch instead of trusting paths from the app
([ADR 0005](adr/0005-elevated-helper-named-pipe.md)).

The install folder is writable by the user. Anyone who can already run code as that user can
change the program that later asks for elevation. This is known and accepted
([SECURITY.md](https://github.com/DotifyBIZ/upkeep/blob/main/SECURITY.md)).

## Where data lives

All under `%LocalAppData%\Upkeep`.

| Path | Content |
|---|---|
| `settings.json` | Preferences and custom cleanup rules. |
| `Logs\upkeep-<date>.log` | Diagnostic log. Files older than 30 days are deleted. |
| `Sessions\` | The session journal that History reads and reverts. |
| `Quarantine\` | Files moved aside instead of deleted. |

## Configuration

There are no environment variables. Settings are in the app, or in `settings.json`. A missing or
corrupt file falls back to defaults. Values are checked on every read.

| Key | Default | Meaning |
|---|---|---|
| `CheckForUpdatesEnabled` | `true` | Check GitHub for a newer release on launch. |
| `LanguageOverride` | empty | Empty follows Windows. `en-US` or `pl-PL`. Anything else is reset to empty. |
| `QuarantineRetentionDays` | `7` | Days before quarantined items are deleted for good. Clamped to 1 to 90. |
| `HasSeenWelcome` | `false` | Set once the welcome tour was shown or skipped. |
| `CustomCleanupRules` | empty list | The user's own rules, such as `D:\Renders\*.cache`. At most 50. Each rule is re-validated on every read. |

## Network

Two things use the network, and neither carries data about the person or the PC
([ADR 0004](adr/0004-update-check-network-call.md)):

- On launch, one unauthenticated `GET` to
  `https://api.github.com/repos/DotifyBIZ/upkeep/releases/latest`. It is on by default and can be
  turned off in Settings. It only shows a banner. Nothing downloads or installs.
- While the Drivers page is in use, Windows Update's own check for driver updates. It uses the
  machine's existing Windows Update configuration, including WSUS or Intune where set.

Nothing else is fetched at run time.

## Operations

- **Logs:** Settings, Logs, "Open logs folder", or read `Logs\` directly. Logging is local only
  ([ADR 0003](adr/0003-local-diagnostic-logging.md)).
- **Backup and restore:** there is nothing to back up centrally. Users revert a session from
  History. Quarantined files can be restored while still in quarantine.
- **Reset:** close Upkeep and delete `settings.json` to return to defaults.
- **Support:** report problems as [GitHub issues](https://github.com/DotifyBIZ/upkeep/issues).
  Security reports go privately to cert@dotify.biz or through GitHub's private vulnerability
  reporting.

## Common failures

- **Warning on install:** expected, the installer is unsigned.
- **Language did not change:** the language is set once at start. Restart Upkeep.
- **An item failed during a cleanup:** a locked file or a refused change does not stop the run.
  The log has the path and the error.
- **Does not start in a remote or headless session:** it needs an interactive desktop.
