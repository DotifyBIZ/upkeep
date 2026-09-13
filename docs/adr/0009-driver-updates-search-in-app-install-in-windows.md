# 9. Search for driver updates in app, install them in Windows

Date: 2026-09-12

## Status

Accepted

## Context

The drivers and updates area has to answer two questions: which drivers on this PC have a newer
version available, and how does the user get it.

Windows exposes driver updates through the Windows Update Agent (WUA) COM API. The full API can
search, download and install, and report that a restart is needed. Driving all of it from Upkeep
would mean late-bound COM inside the elevated helper — `Type.GetTypeFromProgID("Microsoft.Update.Session")`
and `dynamic` calls the compiler cannot check — plus download progress, per-update failure states,
and reboot-required handling. That is the largest and least testable surface in the product, and
every failure mode in it lands on a client machine during a technician's visit.

Windows already has a screen that does the install part well, knows how to resume it after a
restart, and is the surface users and technicians already trust for this.

Driver *enumeration* has its own constraint: `pnputil` prints in the user's display language, and
CLAUDE.md forbids parsing localized tool output — this project is developed on Polish Windows,
where that mistake shows up immediately. `Win32_PnPSignedDriver` returns the same fields through
WMI with stable, non-localized property names.

## Decision

Upkeep searches for driver updates in app, and hands the install to Windows.

- Drivers are enumerated from `Win32_PnPSignedDriver` via WMI, never from `pnputil` output.
- Available updates come from a **read-only** WUA search (`IsInstalled=0 and Type='Driver'`), run
  in the elevated helper as one closed operation. The helper never downloads or installs.
- Installing opens `ms-settings:windowsupdate`, the same hand-off the Performance tab uses for
  Windows' own dialogs.
- Rolling a driver back opens Device Manager for the device. Windows owns the rollback mechanism
  and keeps the previous driver package; reimplementing `DiRollbackDriver` would duplicate it
  without the safety it already has.

Windows Update controls are limited to pausing updates, plus feature and quality update deferral.
Deferral is written to policy keys that Home ignores, so those controls are hidden on Home rather
than shown doing nothing — the edition is read from `EditionID`.

Pause and deferral are machine-wide registry writes, so they are helper operations. `IRegistryProbe`
stays current-user-only: the shell gains no ability to write HKLM (ADR-0005).

## Consequences

- The riskiest code in v1 does not get written. No download state machine, no partial-install
  recovery, no reboot-required plumbing.
- The helper's COM use is one read-only search. If WUA is unavailable or the search fails, the page
  shows the installed drivers without availability rather than failing.
- Upkeep cannot report "installed successfully" for a driver, because it does not install it. The
  page says what is available and sends the user to Windows; History records nothing for an install
  Upkeep did not perform.
- A user on a metered or managed network sees whatever Windows Update policy already allows, which
  is the correct answer rather than one Upkeep invents.
- Pausing and deferring are reversible, so they are journaled like any other reversible change and
  History can put them back. A pause is recorded as the instant it was due to end, and comes back
  as the whole days it had left: the helper request takes days rather than a timestamp, so a revert
  is accurate to the day, not to the second. A pause that lapsed while the session sat in History
  comes back as a resume rather than as a fresh pause nobody asked for.
- This is a smaller promise than "install drivers in app". It was chosen deliberately, with the
  tradeoff understood, after the cost of the alternative was laid out.
