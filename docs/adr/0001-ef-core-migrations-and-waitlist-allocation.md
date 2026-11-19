# 0001 EF Core migrations replace EnsureCreated; waitlist allocation is evaluated lazily

Jira: GTMS-32

## Context
The schema is created by `EnsureCreated()`, which never alters an existing database, so no new table can reach a deployed database. The waitlist feature needs a new table and enum, and a way to hand a released spot to the first person in line without a background worker.

## Decision
1. Use EF Core migrations. The initial migration reproduces today's schema, including the four enums and the `text -> enum` casts (idempotent SQL in the migration). At startup, a database that has the app's tables but no `__EFMigrationsHistory` is baselined (the initial migration is recorded as applied, nothing is changed), then `Migrate()` runs. Migrations are applied by the app at startup, inside the existing process lock.
2. Waitlist holds are stored as rows (`WaitlistEntries`) and evaluated lazily by one shared routine inside a Serializable transaction, called by claim, availability, dashboard and waitlist reads. Hold windows are chained from the previous hold's end (or the release's `ClaimableAt`), not from the time the request arrives, so the result does not depend on which request evaluates.
3. Partial unique indexes are the database-level backstop: one active entry per employee and date, one Offered entry per release.

## Alternatives considered
- Keep `EnsureCreated()` plus hand-written `ALTER` SQL at startup: no tooling, no history, easy to drift and hard to verify. Rejected.
- Background worker for expiry: precise timing, but a new moving part, a second writer racing with requests, and no ticket need. Rejected (ticket allows lazy).
- Hold from "now" at evaluation time: simpler, but the same history yields different `heldUntil` depending on the triggering request. Rejected.
- Run migrations from CI/CD instead of at startup: safer for large changes but the ticket requires startup application.

## Consequences
- Every schema change from now on needs a migration; the `IAppEntity` auto-registration still works but the model snapshot must be regenerated.
- Startup does a baseline check; a failed migration stops the app at startup.
- Reads can write (they expire and offer), so they take a transaction; contention is low and serialization failures are retried once.
- Rollback of a migration is by restoring from backup or a forward fix; the waitlist migration is additive.
