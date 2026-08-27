# FTMS — Financial Transactions Management System

A system of record for financial transactions. Records are **never physically deleted**: every
transaction carries a status, deletion is a transition to `Inactive`, and the application's own
database login is designed to lack the `DELETE` permission that would allow anything else.

ASP.NET Core Web API on .NET 10, Angular 22 SPA, SQL Server, Clean Architecture with DDD.
One repository, one solution, one contract between them.

---

## Prerequisites

| Need | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.300 or later | Pinned in `global.json` |
| Node.js | 22 or later | Developed on 24 |
| SQL Server | LocalDB, or Docker | LocalDB ships with Visual Studio and the SQL Server Express installer |
| Docker | optional | Only needed to run the integration tests, and for SQL Server on non-Windows |

Non-Windows developers have no LocalDB. Start SQL Server from the repo root instead, then point
`ConnectionStrings:FtmsDatabase` at it (see the connection string at the bottom of the file):

```bash
docker compose up -d sqlserver
```

## Run the backend

```bash
dotnet restore
dotnet tool restore          # dotnet-ef, pinned in .config/dotnet-tools.json
dotnet run --project src/FTMS.Api
```

The API listens on <http://localhost:5150>. **In Development it applies migrations on startup**,
so the database is created and the five statuses are seeded on the first run. In every other
environment migrations run at deployment time under a separate elevated login, because the
application's own login has no DDL rights (design doc 06 §5.1).

Useful once it is up:

- <http://localhost:5150/swagger> — Swagger UI, Development only
- <http://localhost:5150/openapi/v1.json> — the contract both clients generate from
- <http://localhost:5150/health> — health check, anonymous by design

There is no login yet. A **development-only** token endpoint stands in so the stack runs end to
end (design doc 06 §3 marks the real work):

```bash
curl -X POST http://localhost:5150/api/dev/token \
  -H 'Content-Type: application/json' \
  -d '{"userName":"you","roles":["Manager"]}'
```

Roles are `Capturer`, `Manager`, `Auditor`, `Admin`. Admin deliberately has **no** transaction
rights: separating duty between administering the system and moving money through it is
elementary financial control.

## Run the frontend

```bash
cd clients/ftms-angular
npm install
npm start
```

<http://localhost:4200>, with `/api` proxied to the backend. Details and the client's own
surprises are in [clients/ftms-angular/README.md](clients/ftms-angular/README.md).

## Run the tests

```bash
dotnet test                                    # everything .NET
dotnet test tests/FTMS.Domain.UnitTests        # the doc 02 state machine matrix
dotnet test tests/FTMS.ArchitectureTests       # the doc 03 dependency rule
dotnet test tests/FTMS.Api.IntegrationTests    # real SQL Server, needs Docker

cd clients/ftms-angular
npm run test:ci                                # Vitest
npm run e2e                                    # Playwright journey (skipped until real auth)
```

**Integration tests need Docker.** They run against a real SQL Server 2022 container via
Testcontainers, never SQLite, because the design leans on `rowversion`, filtered indexes, ledger
tables and the migration pipeline and SQLite can validate none of them (design doc 08 §3).
Without a Docker daemon they report **skipped** rather than failed — nothing was proven, as
opposed to something was proven broken.

## Migrations

```bash
dotnet ef migrations add <Name> \
  --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure
```

`FtmsDbContextFactory` lets the EF tooling build a context without booting the API. Override its
connection string with the `FTMS_DESIGNTIME_CONNECTION` environment variable.

## Layout

```
ftms/
├── src/
│   ├── FTMS.SharedKernel/     Entity, ValueObject, SmartEnum, Result, status ids. No references.
│   ├── FTMS.Domain/           Transaction aggregate, Money, the guarded state machine.
│   ├── FTMS.Application/      Commands, queries, handlers, decorators, the dispatcher.
│   ├── FTMS.Infrastructure/   EF Core, the audit interceptor, caching, migrations.
│   └── FTMS.Api/              Controllers, ProblemDetails, auth, OpenAPI. Composition root.
├── clients/ftms-angular/      The delivered client (design doc 09).
├── tests/                     Domain, Application, Api integration, Architecture.
├── docs/                      Design docs, ADRs, OpenAPI snapshots, runbooks.
└── docker-compose.yml         SQL Server, for developers without LocalDB.
```

Dependencies point **inwards only**, and eleven NetArchTest rules fail the build if that ever
stops being true. Code carries `// design: doc NN` comments pointing back at the chapter that
decided the behaviour.

## Where the docs live

[docs/](docs/) — the ten design docs (01 to 10) move in here, alongside ADRs from this point on,
per design doc 10: everything that describes the system lives in git next to the system, in
plain text formats that diff and review like code.

`docs/api/openapi-v1.json` is the committed contract snapshot. Both the Angular client and any
future WPF client generate their API layers from it, so neither hand-writes DTOs that can drift.

## Deliberately absent

Each of these is a decision with a written trigger, not an oversight. Architecture tests enforce
the first two.

| Not here | Why | What would bring it in |
| --- | --- | --- |
| MediatR | Commercially licensed; the dispatcher is ~100 lines we own | Nothing planned |
| FluentAssertions | Commercially licensed from v8; Shouldly reads nearly as well | Nothing planned |
| Dapper | EF with `AsNoTracking` projections is fast enough | Two consecutive weeks of missed p95 **plus** profiling blaming EF query shape (doc 07 §4) |
| Redis | One API instance next to SQL Server Express | A second API instance |
| WPF client | Angular won the doc 09 matrix on every row that binds | A concrete requirement WPF is strong at and the SPA is weak at |
| Ledger table on `TransactionAudits` | Needs `WITH (LEDGER = ON)` at `CREATE TABLE`, which EF cannot emit | Its own hand-written migration (doc 06 §5.3) |
| `Idempotency-Key` on POST | Needs its own store and retention policy | Doc 05 §5; clients that ignore it lose nothing meanwhile |
| Real authentication | Doc 06 §3 specifies Identity, rotating refresh tokens, TOTP MFA | Next major piece of work |

## Docker connection string

```
Server=localhost,1433;Database=Ftms;User Id=sa;Password=Ftms_Local_Dev_1;TrustServerCertificate=True
```

Put it in `appsettings.Local.json` (gitignored) or `FTMS_DESIGNTIME_CONNECTION`.
