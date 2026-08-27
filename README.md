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
| Node.js | 24 or later | Matches `NODE_VERSION` in CI; npm pinned via `packageManager` |
| SQL Server | Express, LocalDB, or Docker | LocalDB ships with Visual Studio and the SQL Server Express installer |
| Docker | optional | Only needed for the integration tests, and for SQL Server on non-Windows |

Non-Windows developers have no LocalDB. Start SQL Server from the repo root instead, then point
`ConnectionStrings:FtmsDatabase` at it (see [Docker connection string](#docker-connection-string)):

```bash
docker compose up -d sqlserver
```

## Quick start

```bash
dotnet restore
dotnet tool restore                      # dotnet-ef, pinned in .config/dotnet-tools.json
dotnet run --project src/FTMS.Api        # http://localhost:5150

# in a second terminal
cd clients/ftms-angular && npm install && npm start   # http://localhost:4200
```

Open <http://localhost:4200> and sign in as `manager` / `Manager#2026`.

## Set up the database

Two paths. **They produce the same schema — pick one, do not run both.**

**A. EF Core migrations (default).** In Development the API applies migrations on startup, so
`dotnet run` creates the database, seeds the five statuses and the four roles, and creates the
demo accounts. Nothing else to do. To apply them by hand:

```bash
dotnet ef database update --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure
```

**B. Plain SQL, no .NET SDK.** For review with only SSMS or `sqlcmd`:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -i db/scripts/01-create-database.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Ftms -i db/scripts/02-schema-and-seed.sql
```

`02-schema-and-seed.sql` is idempotent — safe to re-run. See [db/scripts/README.md](db/scripts/README.md).

In any environment other than Development, migrations run at deployment time under a separate
elevated login, because the application's own login has no DDL rights (design doc 06 §5.1).

> **On the database name.** The brief suggests `FinancialTransactionsDb` by example; this repo
> uses **`Ftms`** consistently across `appsettings.json`, `docker-compose.yml` and CI. To change
> it, edit the `Database=` segment of `ConnectionStrings:FtmsDatabase`.

## Signing in

Authentication is **ASP.NET Core Identity, self-hosted**, with its tables in this database.
Passwords are PBKDF2-hashed by Identity; accounts lock for 15 minutes after 5 failed attempts.

A session is a 15-minute access token (JWT, held in memory by the SPA) plus a rotating,
single-use refresh token in an **httpOnly, Secure, SameSite=Strict cookie**. Presenting a refresh
token twice revokes the entire session — see [Design decisions](docs/design/decisions.md#06--security).

Four accounts are seeded **in Development only**, on startup from the `Identity:SeedUsers`
section of [`src/FTMS.Api/appsettings.Development.json`](src/FTMS.Api/appsettings.Development.json)
— one per role:

| Username | Password | Role | Transaction rights |
| --- | --- | --- | --- |
| `capturer` | `Capturer#2026` | Capturer | Read, create, update |
| `manager` | `Manager#2026` | Manager | Everything Capturer can, plus soft delete |
| `auditor` | `Auditor#2026` | Auditor | Read only, including Inactive records and the audit trail |
| `admin` | `Admin#2026!x` | Admin | **None** — user administration only |

Admin's exclusion is deliberate: separating duty between administering the system and moving
money through it is elementary financial control.

### Calling the API by hand

[`src/FTMS.Api/FTMS.Api.http`](src/FTMS.Api/FTMS.Api.http) is a complete, runnable request
collection covering every endpoint — open it in Visual Studio, Rider, or VS Code with the REST
Client extension and send the requests top to bottom. With curl:

```bash
TOKEN=$(curl -s -X POST http://localhost:5150/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"userName":"manager","password":"Manager#2026"}' | jq -r .accessToken)

curl http://localhost:5150/api/transactions -H "Authorization: Bearer $TOKEN"
```

Swagger UI at <http://localhost:5150/swagger> has an **Authorize** button — paste the same
`accessToken` into it.

Also useful once the API is up:

- <http://localhost:5150/openapi/v1.json> — the contract both clients generate from
- <http://localhost:5150/health> — health check, anonymous by design

## Run the tests

```bash
dotnet test                                    # everything .NET
dotnet test tests/FTMS.Domain.UnitTests        # the doc 02 state machine matrix
dotnet test tests/FTMS.ArchitectureTests       # the doc 03 dependency rule
dotnet test tests/FTMS.Api.IntegrationTests    # real SQL Server, needs Docker

cd clients/ftms-angular
npm run test:ci                                # Vitest
npm run e2e                                    # Playwright journeys (needs the API running)
```

**Integration tests need Docker.** They run against a real SQL Server 2022 container via
Testcontainers, never SQLite, because the design leans on `rowversion`, filtered indexes, ledger
tables and the migration pipeline and SQLite can validate none of them (design doc 08 §3).
Without a Docker daemon they report **skipped** rather than failed — nothing was proven, as
opposed to something was proven broken. CI fails the build if they skip there.

## Migrations

```bash
dotnet ef migrations add <Name> \
  --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure \
  --output-dir Persistence/Migrations

# then regenerate the plain SQL path so the two stay in step
dotnet ef migrations script --idempotent \
  --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure \
  --output db/scripts/02-schema-and-seed.sql
```

`FtmsDbContextFactory` lets the EF tooling build a context without booting the API. Override its
connection string with the `FTMS_DESIGNTIME_CONNECTION` environment variable.

## Layout

```
ftms/
├── src/
│   ├── FTMS.SharedKernel/     Entity, ValueObject, SmartEnum, Result, status and role ids.
│   ├── FTMS.Domain/           Transaction aggregate, Money, the guarded state machine.
│   ├── FTMS.Application/      Commands, queries, handlers, decorators, the dispatcher.
│   ├── FTMS.Infrastructure/   EF Core, the audit interceptor, caching, Identity, migrations.
│   └── FTMS.Api/              Controllers, ProblemDetails, auth, OpenAPI. Composition root.
├── clients/ftms-angular/      The delivered client (design doc 09).
├── db/scripts/                Plain SQL path to the same schema, for review without the SDK.
├── tests/                     Domain, Application, Api integration, Architecture.
├── docs/                      Design decisions, ADRs, OpenAPI snapshot.
└── docker-compose.yml         SQL Server, for developers without LocalDB.
```

Dependencies point **inwards only**, and eleven NetArchTest rules fail the build if that ever
stops being true. Code carries `// design: doc NN` comments pointing at the corresponding section
of [docs/design/decisions.md](docs/design/decisions.md).

## Deviations from the brief

Every one of these is a decision, and each is defensible in review.

| The brief says | This does | Why |
| --- | --- | --- |
| `GET /api/transactions` returns *a list* of active transactions | Returns a **paged envelope** (`items`, `page`, `pageSize`, `totalCount`), defaulting to Active | An unbounded list over a growing financial table is a production incident waiting to happen. Called bare it behaves exactly as the brief describes; the paging is additive |
| `PUT /api/transactions/{id}` updates a transaction | Same, and **optionally** accepts `If-Match` | Without the header it is last-write-wins, exactly as the brief describes. With it, a stale ETag gets 412 instead of silently overwriting a colleague. Clients that can send it, should |
| Full auth not required | **Every endpoint requires a bearer token** except login, refresh and health | The brief asks you to "consider how you might approach it"; this is that answer, built |
| Database e.g. `FinancialTransactionsDb` | `Ftms` | The brief's name is an example. One name, used consistently |
| DataGrid shows `TransactionStatusName` | Shows the status; the DTO field is `status` | Same value, shorter contract name |
| Grid shows `Id` | Shows the **last segment**, full value on hover and one click to copy | These are GUIDv7: the leading characters are a timestamp, so rows captured in the same minute share them. The trailing segment is the half that actually distinguishes rows |

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
| TOTP MFA | Identity ships the primitives; the enrolment and recovery-code flows are real work | Doc 06 §3 requires it for privileged roles |
| Password reset / user admin UI | No mail transport in scope | A deployment target with SMTP or a mail API |

## Docker connection string

```
Server=localhost,1433;Database=Ftms;User Id=sa;Password=Ftms_Local_Dev_1;TrustServerCertificate=True
```

Put it in `appsettings.Local.json` (gitignored) or `FTMS_DESIGNTIME_CONNECTION`.
