# 0003. Self-hosted ASP.NET Core Identity with rotating refresh tokens

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Brody
- **Relates to:** doc 06 §3 (authentication), doc 06 §4 (transport and abuse), doc 05 §9 (the contract), doc 08 §3 (integration tests)

## Context

Login was built as an extra beyond the brief, and what shipped was not authentication.
`POST /api/dev/token` minted a **Manager** token for any username, verifying nothing:

```csharp
var userName = string.IsNullOrWhiteSpace(request?.UserName) ? "dev.user" : request.UserName;
var roles = request?.Roles is { Length: > 0 } requested
    ? requested.Where(FtmsRoles.All.Contains).ToArray()
    : [FtmsRoles.Manager];
```

The caller chose their own roles. There was no users table, no password hashing, no refresh
token, and the Angular login screen had **no password field** — a username box and a role
dropdown. It was Development-gated and candidly labelled as a stub in three separate places, but
it was also baked into the committed public OpenAPI contract, which declared no security schemes
at all while every other endpoint required a bearer token.

The token was held in memory with no way to renew it, so a page reload signed the user out
mid-task.

Doc 06 §3 had already specified the intended design, and
`Microsoft.AspNetCore.Identity.EntityFrameworkCore` was already version-pinned in
`Directory.Packages.props` — referenced by no project. The intent was declared and never wired.

## Options

**A. ASP.NET Core Identity, EF stores, in the existing `FtmsDbContext`.** Seven `AspNet*` tables
alongside the transaction schema. **Chosen.**

**B. Identity in a second `DbContext` over the same database.** Keeps the transaction schema and
its promise-verifying tests untouched, at the cost of two migration histories, a second
`MigrateAsync` at startup, a second Respawn configuration — and no foreign key from
`RefreshTokens` to the user table, since EF cannot express a cross-context relationship.

**C. Hand-rolled `Users` / `Roles` / `UserRoles` / `RefreshTokens`, using only
`IPasswordHasher<T>`.** Four lean tables, modelled as domain aggregates, fitting the repository's
low-dependency ethos. Rejected: it means owning lockout, security stamps and normalisation, and
this repository's willingness to hand-roll a hundred-line dispatcher does not extend to
credential handling.

## Decision

Option A, plus a `RefreshTokens` table of our own.

**Why one context.** A second context separates tables that share a connection, a backup and a
restore anyway. Option B's only real advantage was leaving the architecture tests undisturbed —
and they were undisturbed regardless: the layering rules forbid EF Core and ASP.NET Core in
*Domain and Application*, and say nothing about Infrastructure, which is precisely the ring where
a framework-shaped persistence concern belongs. All eleven rules pass unchanged.

**Why `AddIdentityCore`, not `AddIdentity`.** The full `AddIdentity` registers a cookie
authentication scheme and makes it the application default, silently displacing JWT bearer. Every
`[Authorize]` would begin redirecting to a login page that does not exist instead of returning
401.

**Why our own refresh token table.** Identity's `AspNetUserTokens` is a key/value bag with no
expiry, no rotation chain and no revocation semantics. All three are requirements.

**Why the token is a database row and not a second JWT.** Revocability. That is the entire
reason, and it is the property that makes a 14-day session acceptable when a 15-minute access
token is not.

## Consequences

**The session design.** 15-minute access JWT held in memory by the SPA; a rotating, single-use
refresh token in an `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/auth`. Only a
SHA-256 of the token is stored — a refresh token is a bearer credential, and a leaked backup must
not hand over live sessions.

**Replay revokes the chain.** A refresh token presented twice means two parties hold it and one
is not the user. We cannot tell which, so both sessions end. Rotation is a single conditional
`UPDATE` guarded on `UsedAtUtc IS NULL`, so two concurrent refreshes cannot both mint a successor.

**`SameSite=Strict` is the CSRF control**, and it is affordable only because the SPA is served
same-origin with the API. If that ever changes, this ADR is superseded and a double-submit token
becomes necessary.

**The reload bug is fixed as a side effect.** The route guard trades the surviving cookie for a
new access token before the route resolves.

**Client refreshes must be coalesced.** Single-use tokens mean parallel 401s each starting their
own refresh would present a spent token — indistinguishable from theft, and the session would be
revoked for being busy. `AuthService` shares one in-flight promise; a unit test pins it.

**Rate limits became configurable.** The strict login bucket was hard-coded at 10 per 5 minutes
and locked the end-to-end suite out of the application on its first run. A threshold that cannot
be tuned per environment is one that eventually gets deleted by whoever it inconveniences.

**Deleted:** `DevelopmentTokenEndpoint`, its OpenAPI operation, and its generated client
function. An integration test asserts `/api/dev/token` now returns 404 — in the Development
environment, the only one it was ever mapped in.

**Still outstanding:** TOTP MFA for privileged roles (doc 06 §3 requires it), password reset, and
a user administration surface. The `AspNetUserTokens` table is created and unused, which is where
MFA's secrets will live.
