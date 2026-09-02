# FTMS design decisions

The code carries **305 `// design: doc NN section X` comments**. This is the document they point
at. One file with ten numbered chapters, rather than ten files, because the citations only ever
needed somewhere to resolve to — and one document that exists beats ten that are planned.

Each chapter keeps the section numbering the code already cites, so
`design: doc 05 section 6` is [§05.6](#056--update). Where a decision has since changed, the
chapter says so rather than quietly reading as though it were always this way.

> **Status.** Consolidated from the decisions embodied in the code, the two ADRs under
> `docs/architecture/adr/`, and the root `README.md`. It is a record of what was decided and why,
> written to be checkable against the code — not a specification written in advance of it.

---

## 01 — Scope and the shape of the problem

FTMS is a **system of record**. Its job is to know what happened and to be able to prove it
later. That single sentence drives most of what follows: audit before convenience, refuse before
guess, and never lose a row.

**Decision 1 — the transaction is the aggregate.** One table, one aggregate root, one
consistency boundary. There is no ledger, no double entry, no balance. The brief asks for records
of transactions, not for an accounting system, and inventing one would be scope no reviewer asked
for.

**Decision 2 — status is a lookup table, not an enum column.** The brief specifies a
`TransactionStatuses` table and a foreign key. Keeping it means referential integrity is enforced
by the database rather than by hope, and statuses can gain columns (display order, terminal flag)
without a schema rewrite.

**Decision 3 — five statuses, not two.** `Active` and `Inactive` are what the brief needs.
`Pending`, `Completed` and `Cancelled` exist because a transaction that can only be alive or
archived cannot express the states a finance team actually works in. The state machine in
[§02.6](#026--the-state-machine) is what keeps the extra states from becoming a free-for-all.

**Decision 4 — nothing is ever physically deleted.** `DELETE /api/transactions/{id}` moves the
status to `Inactive` and leaves the row exactly where it was. This is enforced in three
independent places, deliberately: the handler calls `Deactivate()` rather than removing anything;
the audit interceptor throws if it ever sees an `EntityState.Deleted` on a transaction; and the
application's own SQL login is specified to lack `DELETE` on that table
([§06.5.1](#0651--database-permissions)). One control is a convention; three is a guarantee.

---

## 02 — Data model

### 02.1 — Column choices

**1.1 — Money is `DECIMAL(18,2)`, never `FLOAT`.** Binary floating point cannot represent 0.01,
so a `FLOAT` column loses cents at scale, silently, in a system whose entire purpose is being
right about money. The `Money` value object rounds to two places on the way in and the column
refuses anything wider.

**1.2 — Currency is `CHAR(3)`, defaulting to `'ZAR'`.** ISO 4217 codes are exactly three
characters, so a fixed-width non-Unicode column is both smaller and self-validating. FTMS records
which currency a transaction was in; it does not convert between them, which is why `Money`
offers no arithmetic operators at all. A domain test asserts that absence by reflection, so
adding one is a deliberate act rather than an afternoon's convenience.

**1.4 — Every timestamp is UTC, stored as `datetime2(3)`.** `datetime2` carries no timezone, so
EF materialises `DateTimeKind.Unspecified` and `System.Text.Json` then writes a timestamp with no
trailing `Z`. A browser reads that as local time — two hours wrong on every record in SAST, with
no error anywhere. Two value converters stamp `Kind = Utc` on read, and a JSON converter forces
the `Z` on write. The trailing `Z` is contract, not cosmetics.

**1.5 — Ids are GUIDv7, assigned by the application.** Client-assigned ids mean the aggregate is
whole before it reaches the database, so `Transaction.Create` can return a complete object rather
than one waiting for an identity round trip. Version 7 rather than 4 because its leading 48 bits
are a millisecond timestamp, so ids sort by creation order — which matters for index locality
(see [§02.5](#025--indexing)) and shows up in the UI ([§09.3](#093--showing-an-id)).

**1.6 — `ModifiedAtUtc` is nullable.** Null means "never modified", which is information. A
sentinel equal to `CreatedAtUtc` would be a lie that no query could later untangle.

**1.7 — Every change writes an audit row.** `TransactionAudits` records the change type, a JSON
before-and-after snapshot, who made it and when. It is written by an EF `SaveChanges`
interceptor rather than by handlers, which is precisely what makes it unconditional: no code path
can forget it, because no code path is asked to remember.

Two details matter more than they look. The before-snapshot reads `OriginalValues` from the owned
`Money` entry rather than the tracked entity, because the tracked entity has already been
mutated — a naive implementation records the new amount as the old one, which is not a rounding
error but a false record. And `ChangedBy` is clamped to the column width, because an over-long
value would fail the whole `SaveChanges` and take the business write down with the audit row; the
audit trail must never be the reason a legitimate transaction cannot be saved.

**1.8 — `RowVersion` is a SQL Server `rowversion`.** The database maintains it, so it cannot be
forgotten or faked. It surfaces to clients as an HTTP `ETag` ([§05.6](#056--update)).

### 02.2 — The tables

| Table | Purpose |
| --- | --- |
| `TransactionStatuses` | The brief's lookup. Five rows, seeded, fixed ids |
| `Transactions` | The brief's main table |
| `TransactionAudits` | Append-only change history |
| `AspNet*` (7 tables) | ASP.NET Core Identity ([§06.3](#063--authentication)) |
| `RefreshTokens` | Rotating, revocable sessions |

### 02.3 — Constraints

`CK_Transactions_Amount` enforces `Amount >= 0`. `FK_Transactions_TransactionStatuses` uses
`ON DELETE NO ACTION`: a status must not be removable while transactions reference it.
`UQ_TransactionStatuses_StatusName` keeps the names unique, since the API accepts a status by
name.

`RefreshTokens` is the one place with `ON DELETE CASCADE` — deleting a user should take their
sessions with them, whereas a transaction is a financial record that must outlive whoever
captured it.

> **Changed.** `Money.Create` originally accepted zero while `CreateTransactionValidator`
> required greater than zero, so the two layers disagreed about what a valid transaction was and
> which rule you met depended on how you got there. The domain is now strictly positive. The
> CHECK constraint stays at `>= 0`: a database constraint is a backstop against corruption, not
> the place to express a business rule the domain already owns.

### 02.4 — Deterministic seeding

Statuses and roles are seeded through `HasData` with **fixed GUIDs**, never `NEWID()`. Three
reasons: migrations must produce identical rows in every environment; the application can
reference well-known ids as constants without a lookup on every request; and the filtered index
in [§02.5](#025--indexing) hard-codes the Active id in its `WHERE` clause. Changing a value in
`TransactionStatusIds` or `FtmsRoleIds` is a breaking data migration, not an edit.

Users are **not** seeded this way. A password hash is salted, so it differs on every run and EF
would see a permanent model difference. `IdentitySeeder` creates the demo accounts at startup, in
Development only.

### 02.5 — Indexing

The primary key on `Transactions` is **nonclustered**, which looks wrong until you know that SQL
Server orders `uniqueidentifier` by its *last six bytes*. Even a sequential GUIDv7 therefore
clusters badly, scattering inserts across the B-tree instead of appending. The clustered index
goes on `(CreatedAtUtc, Id)` instead — genuinely increasing, and the order most queries want.

`IX_Transactions_Active_Date` is a filtered covering index emitted as raw SQL, because EF cannot
express `INCLUDE` with a filter. It serves the default list query — active transactions, newest
first — without touching the base table at all.

### 02.6 — The state machine

| From | May become |
| --- | --- |
| Active | Pending, Completed, Cancelled, Inactive |
| Pending | Active, Completed, Cancelled, Inactive |
| Completed | Inactive |
| Cancelled | Inactive |
| Inactive | *(terminal)* |

`Active` and `Pending` are *working* states and accept edits; the rest are history and refuse
them with 409. The table is asserted directly by a `[Theory]` matrix in the domain tests, and a
second test asserts that the aggregate's own methods agree with the table — so a method that
quietly permitted an extra transition would fail the build rather than the audit.

`Deactivate()` is idempotent: deactivating an already-inactive transaction succeeds and changes
nothing, which is what makes `DELETE` safely retryable.

---

## 03 — Architecture

**Section 1 — `FTMS.Api` is the composition root and nothing else.** Controllers build a message,
dispatch it, and translate the `Result`. Every rule, validation and authorization check lives
behind that boundary.

```
SharedKernel  ←  Domain  ←  Application  ←  Infrastructure
                                   ↖___________________ Api
```

Dependencies point inwards only.

**Decision 1 — a deliberately short dependency list.** MediatR and FluentAssertions moved to
commercial licences; AutoMapper, Dapper and Redis have written triggers rather than premature
adoption. An architecture test asserts none of them appear in any referenced assembly, so the
decision cannot erode by accident.

**Section 3 — CQRS with a hand-rolled dispatcher.** Roughly a hundred lines replacing MediatR.
For a system with this many endpoints the mediator is not the hard part, and the interfaces
deliberately mirror MediatR's shape closely enough that migrating later would be mechanical.

**Section 4 — expected failures are values, not exceptions.** `Result` and `Result<T>` carry an
`Error` with a stable code and a type that maps to a status code. A bad password, a missing
record and an illegal transition are all *expected outcomes* of a request; throwing for them
means the happy path is written in exceptions and the logs fill with noise that isn't.

**Section 5 — persistence is a detail.** The Application layer declares what it needs
(`ITransactionRepository`, `ITransactionReadStore`, `ICacheService`, `IIdentityService`,
`IRefreshTokenStore`, `IAccessTokenIssuer`) and Infrastructure supplies it. Architecture tests
fail the build if Application ever references EF Core, SqlClient or ASP.NET Core.

**Section 6 — the change and the row that records it are one transaction.** The audit interceptor
is attached to the `DbContext` options, so it runs inside the same `SaveChanges`. Neither can be
committed without the other.

**Section 7 — caching is opt-in per query.** A query implements `ICachedQuery` to declare a key
and a lifetime. The status list caches for 24 hours (it is effectively immutable); the
transaction list caches for 45 seconds per query shape; get-by-id is deliberately **not** cached,
because correctness beats a micro-saving on a primary key lookup — and because it is the endpoint
that supplies the ETag.

The decorator pipeline runs `Logging → Validation → Caching → Handler`. Validation sits *outside*
caching on purpose: an invalid status must never produce a cache key.

**Section 8 — SharedKernel is the innermost ring.** Everything depends on it, so churn there
ripples everywhere. It holds primitives and constants, and no behaviour worth arguing about.

**Decision 6 — the dependency rule is executable.** Eleven NetArchTest rules fail the build. A
dependency rule that nothing enforces is a diagram.

---

## 04 — Clients

**Decision 3 — clients are thin by contract.** The API owns all behaviour. A client guard, a
disabled button and a hidden column are **usability affordances, never security controls** —
every endpoint re-checks its policy regardless. Anything a client can turn off in devtools is not
protecting anything.

**Section 5 — one generated API layer.** Both the delivered Angular client and any future WPF
client generate their API layer from the committed OpenAPI snapshot, so neither hand-writes DTOs
that can drift from the server. CI regenerates the client and fails if the result differs from
what is committed.

**Section 6 — the client mirrors the server's staleness contract.** See
[§07.6](#076--the-45-second-staleness-contract).

---

## 05 — API contract

### 05.1 — Shape and errors

camelCase properties. UTC ISO 8601 timestamps with a trailing `Z`. Every failure is an RFC 9457
`ProblemDetails` with a stable `type` URI derived from the error code, a `traceId`, and — for
validation failures — a per-field `errors` dictionary.

Error **codes are part of the public contract**. Renaming one is a breaking change for clients,
which is why `DomainErrors` names them once and the API layer aliases rather than retypes them.

Status mapping: `NotFound → 404`, `Validation → 400`, `Conflict → 409`, `Unauthorized → 401`,
`Locked → 423`, everything else `→ 500` with the detail scrubbed. The one special case is the
concurrency conflict code, which maps to **412** rather than 409 — the error *type* cannot
distinguish it from an illegal transition, so it is identified by its stable code.

### 05.2 — Statuses

`GET /api/transactionstatuses` returns all five. No paging: the set is tiny and effectively
immutable, which also makes it the ideal cache-warming call for a client.

### 05.3 — List

`GET /api/transactions`. **Called bare it returns Active transactions only, exactly as the brief
requires.** The query parameters are additive: `status`, `page`, `pageSize`, `sortBy`,
`sortDirection`.

It returns a paged envelope rather than a bare array. An unbounded list over a table that grows
forever is a production incident waiting to happen, and adding paging later would be the breaking
change. `pageSize` is capped at 200 server-side.

> **Changed.** The sort parameter was `sortDir` while the command property was `SortDirection`.
> FluentValidation keys its failures from the property name, so a bad value produced a 400
> complaining about `sortDirection` — a field no caller had ever heard of and no client could map
> to an input. The query parameter is now `sortDirection`.

### 05.4 — Get by id

`GET /api/transactions/{id}` returns a transaction in **any** status, including `Inactive`,
because this endpoint is the audit window. It sets a strong `ETag` from the `RowVersion` and
honours `If-None-Match` with 304.

The ETag travels in the header, not the body. It is HTTP metadata about the representation, and
putting it in the payload would invite clients to persist it as though it were data.

### 05.5 — Create

`POST /api/transactions`. The server assigns the id, sets the status to `Active` per the brief,
and stamps `CreatedAtUtc` — none of them are inputs. Responds 201 with `Location` and `ETag`.

`Idempotency-Key` is not implemented. It needs its own store and retention policy, and clients
that would have ignored it lose nothing meanwhile.

### 05.6 — Update

`PUT /api/transactions/{id}`. **Only `transactionDate` and `transactionType` are modifiable,
exactly per the brief.** Amount, currency and status are not on the request contract *at all*, so
a client cannot even attempt them — the contract refuses before any validator has to. Status
changes get their own explicit endpoints when workflow arrives, because "correct the date" and
"cancel a transaction" are different business acts with different audit meanings.

`If-Match` carries the ETag. Sent and stale → **412**. Sent and unparseable → **428**. Not sent →
the update proceeds as last-write-wins.

> **Changed, and worth being clear about.** A missing `If-Match` used to be 428. The brief
> specifies a plain `PUT`, and requiring a reviewer to `GET` first before changing a date is not
> what it describes. The cost is real: without the header there is **no** concurrency protection
> at all — the entity is loaded fresh, so the database's own `rowversion` check has nothing stale
> to compare against either. A caller who omits the header will overwrite a concurrent edit and
> be told it succeeded. Clients that can send it, should; the Angular client always does. Both
> behaviours are pinned by integration tests.

### 05.7 — Delete

`DELETE /api/transactions/{id}` moves the status to `Inactive`. The row stays. Idempotent — a
second delete returns 204, not an error, which is what retry logic wants. 404 only when the id
never existed. Manager role only.

### 05.9 — OpenAPI is the single client-facing contract

Published at `/openapi/v1.json`, snapshotted to `docs/api/openapi-v1.json`, and generated from by
every client. Route names double as `operationId`s so generated method names are stable and
readable rather than invented from the path.

A schema transformer describes `decimal` as a plain `number`: .NET's default is
`type: ["number","string"]`, which propagates into every generated client as
`amount: number | string` and forces each consumer to narrow a case that never occurs.

> **Changed.** The document declared **no security schemes at all** while every endpoint but
> three required a bearer token — so generated clients had no notion an `Authorization` header
> existed, and Swagger UI offered no Authorize button. A document transformer now declares
> `bearerAuth`, and an operation transformer applies it from each endpoint's own authorization
> metadata, so the contract cannot drift from the attributes.

---

## 06 — Security

### 06.3 — Authentication

**ASP.NET Core Identity, self-hosted, tables in our own SQL Server.** No third-party directory,
and no hand-rolled cryptography: password hashing is Identity's PBKDF2 at its current iteration
count, and lockout is Identity's.

`AddIdentityCore` rather than `AddIdentity`, and the distinction is load-bearing: the full
`AddIdentity` registers a cookie authentication scheme and makes it the application default,
which would silently displace JWT bearer and turn every 401 into a redirect to a login page that
does not exist.

**Sessions.** A 15-minute access token (JWT, HS256, `ClockSkew = TimeSpan.Zero`) plus a refresh
token that is:

- **Rotating** — every use issues a successor and burns the predecessor.
- **Single use** — a second presentation is an attack signal, not a retry.
- **Revocable** — it is a database row, which is the entire reason it is not a second JWT.
- **Never stored raw** — only a SHA-256 of it. A refresh token is a bearer credential; a leaked
  backup must not hand over live sessions. No salt is needed because the value is already 256
  bits of CSPRNG output and therefore not brute-forceable from its digest.
- **Never readable by script** — it lives in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie
  scoped to `/api/auth`.

`SameSite=Strict` *is* the CSRF control. The refresh endpoint takes no body and reads only the
cookie, so under `Lax` a cross-site POST would be enough to rotate somebody's session. Strict is
affordable because the SPA is served same-origin with the API, so there is no legitimate
cross-site request to break — which is also why there is no double-submit token.

**Replay detection.** Presenting an already-redeemed refresh token means two parties hold it and
one of them is not the user. Since we cannot tell which, the **entire chain is revoked**: the
legitimate user is inconvenienced into signing in again, the attacker gets nothing. Rotation is a
single conditional `UPDATE` (`WHERE UsedAtUtc IS NULL AND RevokedAtUtc IS NULL`) so that two
concurrent refreshes cannot both succeed — a load-modify-save would let both mint a successor
from one token.

**No account enumeration.** An unknown username and a wrong password return byte-identical 401s,
and the unknown-user path deliberately performs a decoy PBKDF2 verification so the two take
comparable time. Without it, "no such user" returns in microseconds while "wrong password" takes
tens of milliseconds, and an attacker can sort a username list into real and fake without ever
guessing a password. Lockout reports 423 — but only *after* the password has been checked, so it
reveals nothing to someone who did not already have it.

> **Changed.** This replaces `POST /api/dev/token`, which minted a **Manager** token for any
> username, verifying nothing. It was Development-gated and honestly labelled, but it was also
> baked into the committed public OpenAPI contract. An integration test now asserts it returns
> 404.

Still outstanding: **TOTP MFA** for privileged roles. Identity ships the primitives; the
enrolment and recovery-code flows are real work.

### 06.3 — Authorization

Four roles and three policies:

| Policy | Capturer | Manager | Auditor | Admin |
| --- | :-: | :-: | :-: | :-: |
| `transactions:read` | ✓ | ✓ | ✓ | |
| `transactions:write` | ✓ | ✓ | | |
| `transactions:delete` | | ✓ | | |

**Decision 2 — Admin is deliberately absent from all three.** Separating duty between
administering the system and moving money through it is elementary financial control.

Role names live in `SharedKernel` because three rings need them: the API builds policies from
them, Infrastructure seeds them, and the tests assert the matrix. One vocabulary cannot drift.

### 06.4 — Transport and abuse

CORS is locked to exact origins with `AllowCredentials` — load-bearing now that the refresh token
is a cookie, since a browser will not send one cross-origin otherwise. HSTS and HTTPS redirection
outside Development.

Two independent rate limits. A **global sliding window** (300/minute) partitioned by user name,
falling back to IP. A **strict fixed window** on `/api/auth/login` and `/api/auth/refresh`,
partitioned by IP rather than username — the attack it blunts is one source trying many accounts,
so keying on the account being guessed would hand the attacker a fresh allowance per guess.
Identity's per-account lockout covers the other direction; neither is sufficient alone.

> **Changed, twice.** `UseRateLimiter` ran *before* `UseAuthentication`, so `User.Identity` was
> unconditionally null and the documented per-user partitioning never happened even once — every
> request fell back to IP, and an office behind one NAT shared a single bucket. And the strict
> login limit was hard-coded, which locked the end-to-end suite out of the application on its
> first run: four browsers signing in within seconds is not an attack. Both thresholds are now
> configurable, because a limit that cannot be tuned per environment is one that eventually gets
> deleted by whoever it inconveniences.

The JWT signing key is validated at startup: at least 32 bytes, and rejected outright outside
Development if it equals the key committed to this repository.

> **Changed.** The previous guard searched the configured value for the substring
> `"development"` — a heuristic pretending to be a control, which passed anything that did not
> happen to contain that word.

### 06.5.1 — Database permissions

The application's SQL login is specified to have no DDL rights and **no `DELETE` on
`Transactions`**, which is what makes [§01 decision 4](#01--scope-and-the-shape-of-the-problem) a
guarantee rather than a convention. Migrations run at deployment time under a separate elevated
login.

The GRANT/DENY half of this is currently **untested**: Testcontainers connects as `sa`, so the
integration suite proves the application never *attempts* a physical delete, not that the
database would refuse one.

### 06.5.3 — Ledger table

`TransactionAudits` should be a SQL Server 2022 append-only ledger table. It is not, because
`WITH (LEDGER = ON)` must appear at `CREATE TABLE` and EF cannot emit it. It needs its own
hand-written migration.

### 06.7 — Logging hygiene

Logs and audit rows carry user identifiers, never tokens and never passwords. The logging
decorator records the message type, outcome, error code and duration — **never payloads** —
because a financial payload in a log file is a POPIA problem that outlives the incident it was
meant to help debug.

---

## 07 — Performance

### 07.1–07.2 — The constraint

SQL Server **Express**: four cores, roughly a 1.4 GB buffer pool, 10 GB per database. Every
decision below follows from that ceiling rather than from general good practice.

### 07.3 — Query shape

Read queries project straight into DTOs with `AsNoTracking`, so EF never materialises an
aggregate it will only throw away. The projections are written inline rather than extracted into
a shared helper, deliberately: EF has to translate the expression tree, and a method call it
cannot see inside becomes a client-side evaluation.

### 07.4 — Pooling, caching, indexes

`AddDbContextPool` — creating a context per request is measurable overhead on four cores. That
choice forces the audit interceptor and `ICurrentUser` to be singletons, because EF builds pooled
options from the root provider and cannot resolve a scoped dependency; `IHttpContextAccessor` is
`AsyncLocal`-backed, so a singleton still sees the current request's principal.

Caching in the API is load-shedding for a database with a small buffer pool. All three write
commands invalidate the `tx:list:` family on success — and **only** on success, since a failed
write must not evict a still-correct cache.

`EnableRetryOnFailure` for transient faults. Note that an explicit transaction must then go
through the execution strategy, or EF refuses rather than risk retrying half a transaction.

### 07.5 — Virtualisation

The transaction grid uses CDK virtual scrolling: the DOM holds a screenful of rows regardless of
page size. This is why the grid is a CSS grid with explicit ARIA roles rather than a `<table>` —
`cdk-virtual-scroll-viewport` must be the scrolling container, and table layout does not survive
the `display: block` that requires.

### 07.6 — The 45-second staleness contract

The client cache mirrors the server's key shape (`tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}`)
and its 45-second TTL exactly, so both agree on how fresh is fresh. Identical keys and an
identical TTL make that a fact rather than an aspiration, and there is a unit test per clause.

> **Changed.** The server-side key register behind `RemoveByPrefix` was `static`, so it was
> shared by every instance in the process while the `IMemoryCache` it described was not — two
> hosts in one test process would invalidate against each other's register.

---

## 08 — Testing

**Section 1 — a real pyramid.** Fast domain tests at the base, then application tests over a real
DI container, then integration tests against real SQL Server, then a handful of journeys.

**Section 2 — the domain has zero infrastructure dependencies**, which is exactly what makes its
tests run in microseconds and why there are a lot of them. Architecture tests ride in the same
band: just as fast, and they fail the build rather than a code review three months late.

**Decision 1 — Shouldly, not FluentAssertions.** Licensing. It reads nearly as well.

**Decision 3 — architecture as executable law.** See [§03 decision 6](#03--architecture).

**Decision 4 / Section 3 — integration tests use real SQL Server, never SQLite.** The design
leans on `rowversion`, filtered indexes, `CHECK` constraints, clustered-index choices and the
migration pipeline, and SQLite can validate none of them. Testcontainers starts one SQL Server
2022 container per assembly; Respawn resets data between tests while leaving seeded reference
data alone.

On a machine without Docker the suite reports **skipped** rather than failed — nothing was
proven, as opposed to something was proven broken. CI greps the `.trx` for
`outcome="NotExecuted"` and fails the build, so a skip cannot hide there.

The suite includes **promise-verifying tests**: that every write leaves exactly the expected audit
rows, that reaching past the aggregate to remove a transaction throws, that the schema matches
the design column-for-column, and that `GetPendingMigrationsAsync()` is empty.

**Section 5 — Playwright covers critical journeys only.** Suites that chase coverage at this
level become the slowest, flakiest thing in CI, and a flaky test in a financial pipeline trains
people to ignore red.

> **Changed.** The journey was `test.skip(true, ...)` awaiting real authentication, and it stayed
> skipped long enough that its selectors rotted: it still called `selectOption()` on controls
> that had become ZardUI comboboxes, so removing the skip would have failed immediately. The
> suite now signs in for real and runs. CI asserts the JSON report shows zero skipped and at
> least one executed, because a skipped suite and a passing one look identical in a summary line.

**Section 7.1 — every CI run audits dependencies and scans for secrets.** NuGet vulnerability
audit, `npm audit`, and gitleaks. Cheap, and the class of problem it catches is the class that
reaches production quietly.

`NuGet.config` pins the source, maps every package pattern to it, and requires signature
validation against nuget.org's repository certificates.

> **Changed.** `signatureValidationMode` was `require` with **no trusted signers configured**,
> which trusts nobody and therefore rejects every package it actually has to download. It went
> unnoticed only because every package the solution needed was already in the machine's global
> cache; the first new dependency on a cold agent would have failed the build with an error that
> reads like a supply-chain compromise.

**Section 7.3 — a quarterly authorisation matrix sweep.** The role/policy table in
[§06.3](#063--authorization) is asserted by an integration test on every run, but segregation of
duty is a claim about *people*, not about policies: it holds only if someone periodically checks
that the accounts assigned to each role are still the right ones. That procedure is owed - see
[../runbooks/](../runbooks/).

**Section 8 — the pipeline is ordered by cost.** Fast feedback first, containers only after the
cheap gates pass, journeys last. A red anywhere stops the line.

**Section 9 — coverage floors of 90% on Domain and Application, 80% overall.** Not yet enforced;
a report-generator step is outstanding. Coverage is a floor and never a goal.

---

## 09 — The client

**Decision 1 — Angular over WPF.** The brief accepts either. Angular won on deployment (no
install), on testability (Playwright and Vitest against the same artefact CI builds), and on
reach. WPF's advantages — native controls, offline — are not things this application needs.

**Section 2 — signals, standalone components, zoneless.** State is signals on a store; there is
no NgRx, because the state here is a page of rows and a query, and a reducer per action would be
ceremony over substance.

### 09.3 — Showing an id

The brief asks the grid to display `Id`. A full 36-character GUID takes a third of the width and
crowds out the columns people actually scan, so it is truncated — to the **last** segment, not
the first, and that is not a stylistic choice. These are GUIDv7
([§02.1](#021--column-choices)), whose leading 48 bits are a millisecond timestamp: rows captured
within about a minute of each other share their first eight hex characters, so the conventional
prefix truncation renders most of a freshly seeded list as visually identical strings. The
trailing segment is random, so it discriminates. The full value is on the accessible name and one
click puts it on the clipboard.

**Section 4 — errors.** One interceptor handles every `ProblemDetails`. Statuses whose caller
must handle them (400 field errors, 412/428 reload-and-reapply, and 401 on the credential
endpoints) are deliberately not toasted, because a toast on top of an inline message is noise.

> **Changed.** `signOut()` did not clear the root-scoped transaction list cache, so signing out
> and back in as somebody else inside the 45-second window served the second user the first
> user's rows straight from memory — no request, and therefore no authorization check, ever
> reached the API.

**Section 5 — the token lives in memory only**, never `localStorage`, which any script on the
page can read. The cost is that a reload loses it; the refresh cookie survives, so the route
guard trades it for a new token before the route resolves and a reload no longer looks like a
logout. Concurrent refreshes are coalesced into one request — refresh tokens are single-use, so
racing ourselves would present a spent token and trigger the replay defence in
[§06.3](#063--authentication).

---

## 10 — Documentation as code

Everything describing the system lives in git next to the system, in plain-text formats that diff
and review like code. ADRs follow MADR under `docs/architecture/adr/` and are immutable once
accepted — superseded, never edited.

This document is the consolidation of that principle: 305 citations in the code pointed at ten
documents that did not exist, which meant every one of them was a dead reference and the rationale
lived only in the reader's guesswork. One document that exists is worth more than ten that are
planned.
