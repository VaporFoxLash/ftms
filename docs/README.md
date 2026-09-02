# FTMS documentation

Everything that describes the system lives in git next to the system, in plain-text formats that
diff and review like code (design doc 10 §1).

```
docs/
├── design/
│   └── decisions.md    The ten design chapters. 305 code comments cite this.
├── architecture/
│   └── adr/            Architecture decision records (MADR). Immutable once accepted.
├── api/
│   └── openapi-v1.json The committed contract both clients generate from.
└── runbooks/           Operational procedure. Still owed.
```

## What is here

- **[`design/decisions.md`](design/decisions.md)** — the design rationale, in ten numbered
  chapters matching the `// design: doc NN section X` citations carried throughout the code.
  Decisions that have since **changed** are marked as such in place, rather than rewritten to
  read as though they were always this way.
- [`architecture/adr/`](architecture/adr/) — one short MADR-style record per day-to-day decision
  from the start of building (doc 10 §2). Three so far.
- `api/openapi-v1.json` — the committed contract snapshot both clients generate their API layer
  from (doc 05 §9). CI regenerates the Angular client from it and fails if the result differs
  from what is committed.

## What is still owed

Stated plainly, so the gap is visible rather than implied by an absence:

- [`runbooks/`](runbooks/) — **empty.** Names the five procedures the design already obliges FTMS
  to have, including the restore drill and the elevated-login migration handover.
- `architecture/workspace.dsl` — the Structurizr C4 source of truth, not yet written.
  Deliberately not stubbed: an empty workspace would render as a diagram of nothing, which is
  worse than the honest absence of one. The layering it would show is described in prose in
  [decisions.md §03](design/decisions.md#03--architecture) and enforced by
  `tests/FTMS.ArchitectureTests`, which is the part that matters most.
