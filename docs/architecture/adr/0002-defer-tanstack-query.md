# 0002. Defer TanStack Query while its Angular adapter is experimental

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Brody
- **Relates to:** doc 03 §3 (the mediator question), doc 05 §6 (concurrency), doc 07 §§4 and 6 (caching)

## Context

Doc 07 §6 promises that "the same 45 second staleness contract as the server cache applies to
the client's own memory of the list, so both clients agree on how fresh is fresh." The server
kept its half: doc 07 §4 caches `tx:list:` entries for 45 seconds and all three commands
invalidate by prefix. The Angular client did not — `TransactionsStore` refetched unconditionally
on every filter change and after every mutation.

TanStack Query is the obvious off-the-shelf answer to that gap, and the evaluation was prompted
by exactly this. Its Angular adapter ships as **`@tanstack/angular-query-experimental`**.

## Options

**A. Adopt TanStack Query now.** `staleTime: 45_000` implements the doc 07 §6 contract in one
line, and brings dedup, background refetch, retry and optimistic updates with it.

**B. Implement the contract in the store we already own.** A root-scoped cache keyed identically
to the server's, roughly seventy lines including comments and tests. **Chosen.**

**C. Do nothing.** Leave doc 07 §6 unimplemented. Rejected — it is a written commitment, and the
cost of honouring it is small.

## Decision

Defer TanStack Query. Implement the staleness contract directly, mirroring the server's key
shape (`tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}`), TTL and prefix invalidation, so
the two caches agree by construction rather than by coincidence.

### Why not now

**The adapter is experimental, and this project has already rejected a library on that basis.**
Doc 03 §3 dismissed Cortex.Mediator as "free, but young, and we would be betting the core of our
architecture on a small project." TanStack Query's *core* is mature; the Angular adapter is not,
and the adapter is the part we would depend on. A package that carries `-experimental` in its
name is telling you its API may move on a minor bump.

**Most of what it offers, FTMS does not need or must not have:**

| Capability | Verdict here |
| --- | --- |
| Stale-time caching | The one real need — and ~70 lines in a store we own |
| Request dedup | Marginal; there is one list query |
| Background refetch | Nice to have, no requirement behind it |
| Optimistic updates | **Actively wrong.** Doc 05 §6 requires `If-Match`, and the server answers 412 when the ETag is stale. Showing an optimistic success the server may reject is precisely the silent-last-writer-wins the design forbids |
| Mutation retry | Unsafe until `Idempotency-Key` exists (doc 05 decision 7, still outstanding). Retrying a POST without one risks a duplicate financial record |

Adopting an experimental dependency to obtain one feature, while having to actively disable two
others as incompatible with the domain, is a poor trade.

## Trigger for revisiting

Written down in the same style as the Dapper (doc 07 §4) and Redis (doc 03 §7) triggers, so this
is reopened on evidence rather than enthusiasm. **All three must hold:**

1. A **second screen** needs the same server state, so cache sharing and dedup start earning
   their keep rather than being theoretical.
2. **Background refetch or window-focus revalidation** becomes a stated requirement, not a
   nice-to-have.
3. The adapter has **dropped the `-experimental` suffix** and published a stable major.

Until all three hold, nobody adds TanStack Query.

If the trigger fires, the migration is contained: `TransactionsStore` and
`TransactionListCache` are the only two files that would change, and `TransactionListCache`
would be deleted outright. That containment is itself part of why deferring is cheap.

## Consequences

**Caching a financial list can hide another user's change for up to 45 seconds.** That is the
contract doc 07 chose, not a new risk, but it becomes visible in the client for the first time.
Two mitigations ship with it: a **Refresh** control, and an "Updated 12s ago" hint so the age is
never a mystery. Mutations still invalidate the whole `tx:list:` family, so a user never sees
stale data caused by their *own* writes.

**`loadOne()` is explicitly not cached.** Doc 07 §4 keeps get-by-id off the cache in favour of
ETag and 304, and it is the call the edit form makes to obtain its `If-Match` value — a cached
ETag would be a stale one, and the server would answer 412 to a user who had done nothing wrong.
There is a test pinning this.

**The cache is root-scoped, the store stays component-scoped.** `TransactionsStore` is provided
by the list component and torn down on navigation, so a cache living there would be discarded on
every visit and the contract would buy nothing. Splitting them gives each the right lifetime:
what the user was looking at (filter, page) resets per visit; what the server told us does not.
