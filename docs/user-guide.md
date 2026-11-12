# User guide

## Install

1. Download `UpkeepSetup-<version>.exe` from the
   [Releases page](https://github.com/DotifyBIZ/upkeep/releases).
2. Run it. You do not need administrator rights.
3. Windows SmartScreen will probably warn you. The installer is not code-signed yet
   ([ADR 0002](adr/0002-installer-innosetup-over-msix.md)). Choose "More info", then "Run anyway".

You need Windows 11 (build 22000 or later), 64-bit. The first launch shows a short welcome tour.
You can replay it from Settings.

## How Upkeep keeps you safe

- **Preview first.** Every scan lists exactly what it would touch. Nothing runs until you confirm.
- **Quarantine, not deletion.** Duplicates, large files and uninstall leftovers move to Upkeep's
  quarantine for 7 days by default. Caches and temp files, which Windows and apps rebuild, are
  deleted. If something cannot be undone, the preview says so.
- **Restore point.** A System Restore point is made before a run that includes a destructive
  system-level action.
- **History.** Every session is recorded. You can revert a whole session in one click.

## Screens

| Screen | What you do there |
|---|---|
| Home | See PC health, the next suggested action, recent sessions and drives. A banner appears when a newer Upkeep exists. |
| Cleanup | Scan for junk and caches, review the list, clean. You can add your own cleanup rules. |
| Files | Find duplicates (matched by content, not name), large and old files, and see where disk space went. |
| Apps | Uninstall desktop and Store apps, then review the folders and registry keys that app left behind. |
| Startup & performance | Turn startup items on or off, see what slows start-up, adjust services, power mode, background apps, visual effects and power plan. |
| Drivers & updates | See installed drivers, search for driver updates, pause or defer feature updates, roll back a driver where Windows kept the old one. |
| Milestones | See your monthly maintenance rhythm and milestones, worked out from History. |
| History | See every session and revert one. |
| Settings | Language, update check, quarantine, welcome tour, logs. |

## Common tasks

### Clean up junk

1. Open Cleanup and start a scan. Scanning changes nothing.
2. Review the categories and the files in them.
3. Confirm. If a category needs administrator rights, Windows asks once.

### Add your own cleanup rule

On Cleanup, add a rule such as `D:\Renders\*.cache`. A rule can cover your profile or another
drive. It cannot cover Windows, another user's account, Upkeep's own data or a whole drive with no
filter. Matching files go to quarantine, not to deletion.

### Find out why start-up is slow

Open Startup & performance and run Speed check. Windows' own start-up timings are read only when
you ask. Items are ranked by the delay Windows measured. After you change something and restart,
Speed check shows how much faster it really got. Nothing is estimated
([ADR 0010](adr/0010-measured-speed-and-where-the-numbers-come-from.md)).

### Undo a change

Open History, pick the session, and revert it. Startup items, services, registry keys, quarantined
files and settings go back. A quarantined file can only come back while it is still in quarantine.

### Change language

Settings, Language: follow Windows, English or Polish. A change takes effect after a restart.

### Turn off the update check

Settings, Updates. See [what Upkeep sends over the network](admin-guide.md#network).

### Report a problem

Settings, Logs, "Open logs folder". Attach the log file to a
[GitHub issue](https://github.com/DotifyBIZ/upkeep/issues) if you choose to. Upkeep never sends it.
Report security problems privately, as described in
[SECURITY.md](https://github.com/DotifyBIZ/upkeep/blob/main/SECURITY.md).

## What Upkeep does not do

- No malware scanning. Windows Defender does that.
- No built-in driver database. Driver updates come from Windows Update.
- No general registry cleaner. Registry cleanup is limited to leftovers of an app you just uninstalled.
- No browser-data cleaner and no file shredder yet
  ([ADR 0008](adr/0008-deferred-privacy-cleaner-and-shredder.md)).

## Questions

**Why the SmartScreen warning?** The installer is unsigned. Windows warns about files it has seen
few times.

**Why does Upkeep ask for administrator rights?** Some actions change the whole machine: system
cleanup, services, Windows Update settings. Upkeep raises one prompt per session for these
([ADR 0005](adr/0005-elevated-helper-named-pipe.md)).

**Is anything sent about me or my PC?** No. See [Network](admin-guide.md#network).
