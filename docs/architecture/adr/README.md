# Architecture Decision Records

Design docs 01 to 10 captured the big decisions in narrative. From the start of building, day to
day decisions get one short record each in here instead.

design: doc 10 §2 — "one short MADR style file each (context, options, decision, consequences),
numbered and immutable. When a decision is reversed, a new ADR supersedes the old one, and the
trail of changed minds is itself documentation."

## Rules

1. **Numbered sequentially**, four digits: `0001-short-kebab-title.md`.
2. **Immutable once merged.** Fix typos, never rewrite the reasoning. A record that quietly
   changed its mind is worse than no record, because it looks like you were right all along.
3. **Reversals supersede.** Write a new ADR, set the old one's status to `Superseded by 000N`,
   and link both ways. That line is the only edit an accepted ADR ever takes.
4. **Status** is one of `Proposed`, `Accepted`, `Superseded by 000N`, or `Deprecated`.

## What belongs here

A decision that a future reader would otherwise have to reverse-engineer from the diff, or one
that closed off an option someone will reasonably suggest again. Adding a dependency, rejecting
a dependency, changing a boundary, accepting a tradeoff with a cost attached.

Routine implementation choices do not need one. If the code explains itself, let it.

## Where triggers live

FTMS defers a lot on purpose — Dapper, Redis, WPF, ledger tables, `Idempotency-Key`. Each
deferral carries a **written trigger** saying what would bring it back, so the decision is
revisited on evidence rather than on someone's Tuesday afternoon enthusiasm. When a deferral is
recorded here, the trigger is part of the record, not a footnote.

## Note on location

Doc 10 contradicts itself: the tree in §1 puts ADRs at `docs/architecture/adr/`, while the prose
in §2 says `docs/adr/`. This folder follows the §1 tree, since that is the explicit layout
diagram. Flagging it so the next reader does not assume one of the two is a mistake nobody
noticed.

## Index

| # | Title | Status |
| --- | --- | --- |
| [0001](0001-adopt-angular-cdk.md) | Adopt @angular/cdk for dialog, announcements and virtual scrolling | Accepted |
| [0002](0002-defer-tanstack-query.md) | Defer TanStack Query while its Angular adapter is experimental | Accepted |
| [0003](0003-persistent-authentication.md) | Self-hosted ASP.NET Core Identity with rotating refresh tokens | Accepted |
