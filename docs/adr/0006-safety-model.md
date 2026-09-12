# ADR-0006: The safety model — preview, tiered removal, restore point, session journal

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** `Upkeep.App.Core` (`Safety/`, `Sessions/`, `Quarantine/`), every page that changes anything

## Context

Upkeep deletes files, disables services and edits the registry on machines its authors will never see. The brief specifies four protections — non-destructive defaults, an automatic System Restore point, a dry-run preview, and a session undo log — and says they compose rather than being alternatives.

Applying "non-destructive" literally to everything, though, contradicts the product. Moving 5 GB of temporary files to a quarantine folder frees no disk space at all until that quarantine is emptied, and the whole point of the Cleanup page is the space it reclaims. The Recycle Bin is worse for this: it has a per-drive size cap, and items over it are deleted outright rather than recycled — a safety net with a silent hole in it. Meanwhile some of what Upkeep runs is irreversible no matter what wrapper is around it: emptying the Recycle Bin, `DISM /StartComponentCleanup`, an app's own uninstaller.

## Decision

Four mechanisms, applied together, with removal **tiered by what the item actually is**.

**1. Dry-run preview.** Scanning never changes anything. Every scan produces a plan of concrete, named actions with sizes and reversibility, the user reviews it (down to individual file paths), and only an explicit confirmation executes it.

**2. Tiered removal.**

| Item | Treatment | Why |
|---|---|---|
| Caches, temp files, shader/thumbnail caches, crash dumps | Deleted | Windows and the apps rebuild them; keeping copies would defeat the purpose and reclaim nothing |
| Duplicates, large/old files, uninstall leftovers | Quarantined for 7 days | Real user data — the one category where "I needed that" is a plausible sentence |
| Recycle Bin, component store cleanup, an app's own uninstaller, driver install/rollback | Executed as-is, labelled "can't be undone" in the preview | Windows owns the operation; there is nothing to keep |

Permanent deletion of a quarantine-eligible item stays available as an explicit per-action opt-in.

**3. System Restore point** before any run containing a destructive system-level action. If System Protection is off for the system drive, Upkeep turns it on rather than skipping the protection or blocking the run (the Product Manager's explicit call); the fact that it did is reported afterwards in the results and in the session, not as a prompt beforehand. If Windows' own 24-hour throttle means no new point is created, the most recent existing point is recorded against the session instead of pretending one was made.

**4. Session journal.** Every session writes a manifest to `%LocalAppData%\Upkeep\Sessions\<id>.json`, appended **before** each action executes, holding what it takes to reverse that action — the previous service start type, the previous registry value, the quarantine path a file moved to, the startup entry's previous state. History can revert a whole session in one action; entries that were never reversible say so.

## Options Considered

### Tiered by item kind (chosen)

- Pros: the results screen reports space actually freed; the safety net covers the files where it means something; irreversible work is named as such instead of hidden behind a false promise.
- Cons: deviates from the literal reading of the brief's "everything goes to the Recycle Bin or quarantine".

### Everything to quarantine

- Pros: one rule, no judgement calls, nothing ever immediately unrecoverable.
- Cons: a cleanup frees nothing until the quarantine expires; "7.7 GB freed" becomes "7.7 GB will be freed in a week", which is both worse and harder to explain.

### Everything to the Recycle Bin

- Pros: native and familiar; recoverable with tools the user already knows.
- Cons: the size cap silently drops large items; recycling hundreds of thousands of small temp files is slow; and cleaning the Recycle Bin itself is a feature, which makes it a strange place to put the backups.

## Consequences

A quarantined file occupies disk until it expires, so the results screen distinguishes "freed" from "will be freed when quarantine empties", and Settings offers "Empty now".

Quarantine is per-volume so the move is a rename rather than a copy: `%LocalAppData%\Upkeep\Quarantine` for the system volume, a hidden `\.upkeep-quarantine` at the root of any other volume, and `%ProgramData%\Upkeep\Quarantine` (Administrators/SYSTEM only) for anything the elevated helper removed — see ADR-0005 for why that one can't share the user's.

Restoring is best-effort by definition: a quarantine entry whose 7 days elapsed, or whose original location no longer exists, reports that rather than failing the whole revert.

## Implementation Notes

- The journal is written incrementally, not at the end: a crash mid-run must still leave a revertable record.
- Session manifests are untrusted input on read — they live in a user-writable directory, so a revert validates every path and key through the same policy as the original action, and the elevated helper re-validates independently (ADR-0005).
- Quarantine purge runs at app start for the user-scope store, and through the helper for the machine-scope one.
- "Can't be undone" is a property of the planned action, set by the scanner that produced it — never a UI decision.

## References

- ADR-0005 (elevated helper: why machine-scope quarantine is separate)
- `README.md` "How it stays safe" — the same four points, stated for users
