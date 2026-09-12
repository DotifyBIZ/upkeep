# ADR-0008: Privacy cleaner and secure file shredder — considered, deferred

- **Date:** 2026-09-12
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** Scope of v1; README roadmap

## Context

Two features are conventional in this product category and were considered for v1:

- A **privacy cleaner** — browsing history, cookies, saved form data, recent-documents lists, run history.
- A **secure file shredder** — overwriting a file's bytes before deleting it.

Both were left out. This ADR records why, so a later "why doesn't it do the obvious thing?" has an answer other than oversight.

## Decision

Neither ships in Phase 1. Both are listed in the README's Phase 2 roadmap.

**Privacy cleaner.** Upkeep v1 clears browser *caches* — regenerable data — but not history, cookies or saved logins. The distinction matters: clearing a cache costs the user a slower page load, while clearing cookies signs them out of everything and clearing form data loses things they cannot get back. That is a different consent conversation than "remove temporary files", needs per-item granularity per browser and profile, and would be the first place Upkeep touches data a person might be emotionally attached to. It deserves its own design pass rather than a checkbox in the Cleanup list.

**Secure shredder.** Overwriting is close to meaningless on the storage this product targets. SSDs remap writes through the FTL, so an overwrite lands on different physical cells than the original; wear levelling, over-provisioning and TRIM make single-file sanitization unverifiable from user space. NTFS adds its own copies — the MFT for small resident files, previous versions, and journal remnants. A feature that claims a file is unrecoverable when it may well be recoverable is worse than not shipping it. The honest answers are full-disk encryption or the drive's own secure-erase, neither of which is a per-file button.

## Options Considered

### Defer both, document why (chosen)

- Pros: keeps v1's promise narrow and true; avoids shipping a security claim the storage can't back.
- Cons: two features competitors advertise are absent, and some users will look for them.

### Ship the privacy cleaner in v1

- Pros: expected in a cleaning tool; genuinely useful on a shared machine.
- Cons: irreversible in a way caches are not (sign-outs, lost form data), and the quarantine model doesn't map onto browser databases. Needs its own design, not a row in an existing list.

### Ship the shredder in v1

- Pros: trivial to implement; reassuring to users.
- Cons: the reassurance would be false on SSDs. Not a trade this project will make.

## Consequences

Cleanup's browser section stays strictly cache-only, and says so in the UI. Permanent deletion in Upkeep means "unlinked, not recoverable through Upkeep" — never "unrecoverable", in any string the product shows.

If a privacy cleaner is picked up later, it starts from per-browser, per-profile, per-data-type selection with its own preview, not by adding rows to the existing Cleanup categories.

## References

- ADR-0006 (safety model — the quarantine and preview machinery a future privacy cleaner would have to fit)
- `README.md` — "What it deliberately doesn't do", and the Phase 2 roadmap
