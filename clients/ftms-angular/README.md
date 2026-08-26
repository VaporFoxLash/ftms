# FTMS Angular client

The delivered FTMS frontend. design: doc 09 — Angular won over WPF on reach, deployment,
hiring, testing cost and delivery cost. WPF stays a documented, architecturally supported
alternative with a written revisit trigger; it is not built.

## Running it

The client needs the API. From the repo root:

```bash
dotnet run --project src/FTMS.Api      # http://localhost:5150
```

Then here:

```bash
npm install
npm start                              # http://localhost:4200
```

`npm start` runs the dev server with `proxy.conf.json`, so `/api` reaches the backend on the
same origin. No CORS preflight in development, and the production layout (SPA served as static
files from the API host, design doc 04 §6) behaves identically.

## The API layer is generated, never hand written

design: doc 05 §9 — OpenAPI is the single client-facing contract, and both clients generate
their API layers from it, so neither hand-writes DTOs that can drift.

```bash
npm run generate:api          # needs the API running; snapshots the doc, then generates
npm run generate:api:offline  # regenerate from the committed snapshot only
```

The snapshot lands in `docs/api/openapi-v1.json` and the client in
`src/app/core/api/generated/`. **Both are committed**, on purpose:

- CI builds the frontend without a running backend.
- A contract change shows up as a reviewable diff in the pull request that caused it, which is
  what doc 10 §2 asks for.

Never edit anything under `generated/`. Change the API, regenerate, commit both.

## Tests

```bash
npm run test:ci   # Vitest unit tests
npm run e2e       # Playwright journey (skipped until real auth lands)
```

The Playwright journey is deliberately one test. design: doc 08 §5 — journeys only, because
e2e suites that chase coverage become the slowest, flakiest thing in CI.

## Things that will look odd until you know why

- **A page refresh signs you out.** The access token is held in memory only and never in
  localStorage, because localStorage is readable by any script that reaches the page
  (doc 06 §3). Recovery is a silent refresh against the httpOnly cookie, which lands with real
  Identity.
- **The login screen is a stub** that issues whatever role you ask for. It calls the API's
  development-only token endpoint and must be deleted with it. The role picker exists so the
  doc 06 authorization matrix is testable by hand: sign in as Auditor and archiving refuses,
  sign in as Admin and transactions are refused entirely.
- **Amount and currency are read-only when editing.** Only date and type are modifiable, per
  the brief (doc 05 §6). They are not on the update DTO at all.
- **Archived rows offer only View.** Completed, cancelled and archived records are history, so
  Edit would earn a 409 and Archive would be a no-op.
