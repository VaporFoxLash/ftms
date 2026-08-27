# FTMS documentation

Placeholder. The ten design docs (01 to 10) that specify this system move in here, per
design doc 10 section 1 (docs as code: everything that describes the system lives in git
next to the system, in plain text formats that diff and review like code).

Target layout once the design set is copied in:

```
docs/
├── design/          docs 01 to 10, the architecture set
├── architecture/
│   ├── workspace.dsl   Structurizr, the C4 source of truth
│   └── adr/            architecture decision records going forward
├── api/             generated OpenAPI snapshots per release
└── runbooks/        incident response, restore drills, operations
```

Code throughout the solution carries `// design: doc NN` comments pointing back to the
chapter that decided the behaviour.

## What is here now

- [`architecture/adr/`](architecture/adr/) — architecture decision records. Design docs 01 to 10
  captured the big decisions in narrative; from the start of building, day to day decisions get
  one short MADR style record each (doc 10 §2).
- `api/openapi-v1.json` — the committed contract snapshot both clients generate their API layer
  from (doc 05 §9).
