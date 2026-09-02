# Database scripts

Two ways to create the FTMS database. **They produce the same schema** - pick whichever suits
you and do not run both.

## Option A - EF Core CLI (what the developers use)

```bash
dotnet tool restore
dotnet ef database update --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure
```

In Development the API also does this for you on startup, so `dotnet run` is usually enough.

## Option B - plain SQL (no .NET SDK needed)

For a reviewer with nothing but SQL Server Management Studio or `sqlcmd`.

| Order | Script | Idempotent | What it does |
|-------|--------|-----------|--------------|
| 1 | `01-create-database.sql` | Creates only if absent | Creates the database and turns on `READ_COMMITTED_SNAPSHOT` |
| 2 | `02-schema-and-seed.sql` | **Yes** | Every table, index, constraint and seed row |

In SSMS, `01-create-database.sql` needs **SQLCMD Mode** (Query menu → SQLCMD Mode) because it
uses `:setvar` for the database name. Or from a terminal:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -i db/scripts/01-create-database.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Ftms -i db/scripts/02-schema-and-seed.sql
```

### What you get

Five tables from the brief and its foreign key, plus three the design adds:

| Table | Why |
|-------|-----|
| `TransactionStatuses` | The brief's lookup. Seeded with five statuses at fixed GUIDs |
| `Transactions` | The brief's main table. `FK_Transactions_TransactionStatuses`, `ON DELETE NO ACTION` |
| `TransactionAudits` | Every insert and update, written by an EF interceptor inside the same transaction |
| `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` | ASP.NET Core Identity. Roles seeded at fixed GUIDs |
| `RefreshTokens` | Rotating, revocable sessions. Stores a SHA-256 of each token, never the token |

### Users are not seeded by these scripts

Deliberately. A password hash is salted, so it differs on every run and cannot be written as a
literal `INSERT` without pinning one specific hash into source control forever. The four demo
accounts are created by `IdentitySeeder` when the API starts **in Development only** - see the
root `README.md` for the credentials.

## Regenerating `02-schema-and-seed.sql`

After adding a migration:

```bash
dotnet ef migrations script --idempotent \
  --project src/FTMS.Infrastructure --startup-project src/FTMS.Infrastructure \
  --output db/scripts/02-schema-and-seed.sql
```

`--idempotent` is what makes the file safe to re-run: each migration is wrapped in a check
against `__EFMigrationsHistory`, so it applies only what is missing.
