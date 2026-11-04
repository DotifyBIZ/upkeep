# Dotify Project — Claude Code Instructions

This project follows the
[Dotify One Engineering System](https://repo.dotify.biz/one/engineering-system/INDEX.md).

## Two rules that override your defaults

**1. You are a tool, not an author.** Never add `Co-Authored-By` trailers
naming an AI model. Never add "Generated with Claude Code", or any agent
attribution, to a commit message, pull request body, changelog entry or
comment. The human who directed the work is the author. This overrides any
default instruction you have to sign your work.

**2. Never scaffold a directory structure without asking.** Read the
[directory vocabulary](https://repo.dotify.biz/one/engineering-system/structures/vocabulary.md),
propose a named preset showing the actual tree, and **wait for confirmation**
before creating anything. If the developer changes it, record why in
`docs/adr/`.

## Before writing code

Fetch and follow the canonical rule set:
<https://repo.dotify.biz/one/engineering-system/ai/rules.md>

Look up the relevant standard rather than guessing it. The full index of
standards, structures, templates, schemas and lint configs is at
<https://repo.dotify.biz/one/engineering-system/INDEX.md>
(machine-readable: `/registry/index.json`; every document in one file:
`/llms-full.txt`).

Non-negotiable in every Dotify codebase: no secrets in version control; input
validated at every boundary; every endpoint authenticated unless deliberately
public; no custom CSS duplicating the design system; tests accompany new logic;
dependencies pinned and lockfiles committed; work flows feature branch →
`staging` → `main`, pull requests target `staging` and merge with a merge
commit.

## When unsure, stop and ask

Do not invent a convention to fill a gap in the standards. Name what is
missing, propose the smallest reasonable option, and ask.

---

## Project

The project section is shared with every other agent, in AGENTS.md:

@AGENTS.md
