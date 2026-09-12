# Security Policy

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report privately either way:

- GitHub's [Private Vulnerability Reporting](https://github.com/DotifyBIZ/upkeep/security/advisories/new) (Security tab → Report a vulnerability) — opens a private discussion with maintainers before any details become public.
- Email **cert@dotify.biz**.

Include what you'd include in any good bug report: the affected version or commit, steps to reproduce, and the impact as you understand it.

## Supported Versions

Upkeep ships frequent releases with no long-term-support branch — please report against the [latest release](https://github.com/DotifyBIZ/upkeep/releases/latest), or the latest commit on `main` if you're building from source.

## What to expect

We'll acknowledge new reports and work with you on a fix before any public disclosure.

## Scope

Upkeep is local-first with no server component, so reports will usually concern the desktop application itself. The areas worth the most scrutiny:

- **The elevated helper** (`docs/adr/0005-elevated-helper-named-pipe.md`) — the named pipe's access control, the closed operation set, and the validation each operation applies before touching anything. A way to make the helper act on a path, registry key or service outside what it re-derives for itself is a vulnerability, not a bug.
- **The quarantine and session journal** — restoring a quarantined item must never write outside the location it was taken from.
- **Untrusted input at the boundaries** — session manifests, settings files and anything read back from disk are parsed defensively; so is everything read out of the registry, the package manager and Windows' own command-line tools.

Known and accepted: Upkeep installs per-user, into a directory the signed-in user can write to. Anyone who can already run code as that user can therefore modify the binary that later asks for elevation. This is inherent to per-user installation (see ADR-0002 and ADR-0005), not a defect in the helper protocol.
