# Design documentation

The design rationale lives in **[decisions.md](decisions.md)** — one document, ten numbered
chapters, matching the `// design: doc NN section X` citations carried throughout the code.

There are **305** such citations. They used to resolve to nothing: this folder held only an index
reconstructed from the citations themselves, so every reference in the codebase was a dead link
and the reasoning behind a line of code lived wherever the reader could guess it. One document
that exists beats ten that are planned.

## Chapter index

| Doc | Chapter | Cited |
| --- | --- | ---: |
| 01 | Scope and the shape of the problem | 1 |
| 02 | Data model | 50 |
| 03 | Architecture | 37 |
| 04 | Clients | 5 |
| 05 | API contract | 87 |
| 06 | Security | 61 |
| 07 | Performance | 38 |
| 08 | Testing | 26 |
| 09 | The client | — |
| 10 | Documentation as code | — |

Decisions that have **changed** since they were first made are marked as such in place, rather
than rewritten to read as though they were always this way.

## Also here

- [`../architecture/adr/`](../architecture/adr/) — Architecture Decision Records (MADR).
  Immutable once accepted: superseded, never edited.
- [`../api/openapi-v1.json`](../api/openapi-v1.json) — the committed contract snapshot both
  clients generate from.

## Still owed

- `../architecture/workspace.dsl` — a Structurizr C4 model. The layering is currently described
  in prose in [decisions.md §03](decisions.md#03--architecture) and enforced by
  `tests/FTMS.ArchitectureTests`, which is the part that matters most.
- Runbooks — see [`../runbooks/`](../runbooks/).
