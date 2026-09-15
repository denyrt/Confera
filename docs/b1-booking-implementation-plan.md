# B1: Transactional booking implementation plan

Approved for implementation by the maintainer on 2026-09-16. This document
preserves the complete agreed execution plan in English. Implementation status
belongs in the [roadmap](implementation-roadmap.md); actual validation evidence
is appended below. Approval of this plan alone is not evidence of delivery.

Read alongside [CONTRIBUTING](../CONTRIBUTING.md), the
[agent workflow](agent-workflow.md), [domain model](domain-model.md), and
[booking and pricing rules](booking-and-pricing-rules.md). The original
[technical assignment](task-specification.md) remains unchanged.

## 1. Outcome and scope

Deliver a working `POST /bookings` that checks room availability, calculates
the price, atomically persists a complete booking, and returns confirmation
with a price breakdown. Expose it through Swagger UI and test it against real
PostgreSQL. This completes the booking operation's portion of H1 alongside B1.

The assignment has a seven-day deadline with only today and tomorrow remaining
at approval. Keep implementation focused on delivering business operations.
The agreed scope includes:

- A booking application use case.
- A short Read Committed transaction locking one room row.
- Fresh room/service reads, a single tariff-set read, availability checks,
  atomic snapshot persistence, and database-conflict handling.
- Small Domain validation changes, without changing pricing mathematics.
- HTTP request/response contracts, safe errors, OpenAPI, and Swagger UI.
- Application, PostgreSQL, and HTTP tests, CI integration, and documentation.

No automatic booking-write retries, idempotency keys, new tables, or migrations
are required. R1, A1, and reports remain subsequent tasks; document their
integration requirements. Do not introduce generic repositories, a transaction
framework, MediatR pipelines, distributed locks, queues, outbox, tariff
versioning/global locking, or a new cross-platform infrastructure failure
matrix. Any material extension needs a separate discussion with the maintainer.

The accepted trade-off is that a committed booking whose response is lost may
produce a conflict on a repeated request, rather than replaying the original
confirmation. Never claim a failed response proves that no booking was saved.

## 2. Preparation and affected components

1. Inspect the working tree and fetch `origin/main`, preserving user changes.
2. Confirm completed P1 prerequisites.
3. Create `feature/booking-creation` from the verified remote main using
   `--no-track`, following CONTRIBUTING. Do not publish without authorization.
4. Save this approved plan in documentation and register it in the solution.

| Component | Changes |
| --- | --- |
| Domain | Shared period validation and typed expected booking errors. |
| Application | CreateBookingService, command/result, and a focused persistence contract. |
| Infrastructure | Transaction, room locking, queries, persistence, and provider-error translation. |
| API | Controller, DTOs, timestamp validation, error handling, OpenAPI, and Swagger UI. |
| Tests | Domain/application checks, independent-connection concurrency, persistence, and HTTP. |
| Documentation | This plan, transaction rules, current status, and actual validation evidence. |

## 3. Domain and application contracts

### Shared period validation

Expose a small public Domain API for UTC/microsecond precision, ordered
endpoints, the inclusive 30-minute to 24-hour duration, and a start not before
the supplied current time. Reorganize existing validation so Room.Book,
the calculator, and future availability search use the same rules. A generic
time-interval value-object framework is unnecessary.

### Expected errors

Introduce a compact typed Domain booking error with codes for invalid periods,
invalid service selection, and missing full tariff coverage. Invalid tariff
configuration and broken internal invariants remain internal failures; never
map every ArgumentException to a client error. Limit changes to relevant
validation and preserve calculation, rounding, and snapshot structure.

### Application API

CreateBookingService accepts room ID, UTC start/end, service IDs, and a
CancellationToken. Its dependencies are a focused persistence contract and the
standard TimeProvider. Two small persistence contracts are sufficient:

- Open one operation with a fresh context and transaction.
- Within that transaction: lock/load a room, read tariffs, check overlap,
  save a booking, and commit.

The transaction supports asynchronous disposal. Application coordinates the
operation; EF Core, Npgsql, SQL, and concrete transaction objects remain in
Infrastructure. No repository per entity or generic unit-of-work layer.

## 4. Transactional execution

1. Validate database-independent input: nonempty room ID, valid period,
   a supplied service list, and nonempty/distinct service IDs.
2. Open a fresh DbContext and a Read Committed transaction.
3. Lock the room row by ID with SELECT FOR UPDATE before using room data.
4. Load the room and current services after acquiring the lock. An absent or
   soft-deleted room produces room_not_found. Do not reuse previously tracked
   room or service values.
5. Read the entire tariff configuration in one query and use that set for the
   booking. No guarantee of the newest tariff at commit time is added.
6. Obtain one UTC clock value after waiting for the lock, floor it to whole
   microseconds, and use it for period validation and CreatedAtUtc. If the start
   became past while waiting, reject it.
7. Check overlap with an existence query, without loading booking history:
   `RoomId == roomId && StartsAtUtc < requestedEnd && EndsAtUtc > requestedStart`.
8. Call Room.Book; Domain selects services, prices segments, and creates snapshots.
9. Persist the complete aggregate and commit.
10. Return success only after commit succeeds.

Same-room writes briefly serialize, including bookings for disjoint periods.
Different rooms do not share an application lock. The existing half-open
PostgreSQL exclusion constraint remains the final overlap guard.

### Failures and cancellation

- Translate only the exclusion violation for EX_Bookings_RoomPeriod to the
  same room-unavailable error as the preliminary overlap check. Inspect the
  PostgreSQL error code and constraint name, not exception-message text.
- A failure before commit rolls back the operation and its children.
- Pass cancellation to every database operation. Client cancellation must not
  be reported as database unavailability.
- Use configured command timeouts; do not build another deadline system.
- Transient failures/timeouts produce a safe unavailable result without
  automatic replay. A lost connection during commit may leave its result
  unknown; error wording must not promise absence of a persisted booking.

### R1 integration contract

Room edits, service changes, and deletion must acquire the same room-row lock
before reading/validating/mutating the room. Locking the parent is a shared
application protocol, not a claim that arbitrary SQL updates to child rows
automatically follow it. Full room lifecycle operations remain R1.

## 5. HTTP contract

### Request

POST /bookings accepts these required JSON fields:

| Field | Rule |
| --- | --- |
| roomId | Nonempty UUID. |
| start | ISO 8601 timestamp with Z or an explicit offset. |
| end | The same timestamp requirements as start. |
| serviceIds | UUID array; an empty array is allowed, missing/null is rejected. |

Normalize timestamps to UTC before Application. Validate the original text for
precision finer than a microsecond before parsing can truncate it. Additional
zero fractional digits do not change the represented precision. No attendance
or capacity input is added to booking creation.

### Success

Return 201 Created with bookingId, roomId, start/end in UTC, currency UAH,
totalPrice, rental segments (start/end, pricing code, multiplier, hourly rate,
price), and selected service snapshots (name and price). Build the response
from the created booking. Sort segments by start and services deterministically.
Do not add a Location header pointing to an unimplemented booking retrieval API.

### Errors

Use ProblemDetails with a stable code extension:

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | invalid_request | Invalid JSON, required fields, UUID or timestamp format. |
| 400 | invalid_booking_period | Invalid period or start in the past. |
| 400 | invalid_service_selection | Duplicate, empty, unknown, or unavailable service IDs. |
| 400 | tariff_coverage_missing | The whole period is not covered. |
| 404 | room_not_found | Absent or soft-deleted room. |
| 409 | room_unavailable | Overlapping booking. |
| 503 | booking_persistence_unavailable | Recognized temporary database failure or timeout. |
| 500 | internal_error | Unexpected internal error. |

Model-binding and application errors share the same response shape. Do not
expose SQL, stack traces, or provider exception details.

### OpenAPI and Swagger UI

Connect Swagger UI to the existing /openapi/v1.json in Development. Document
required fields, UTC/precision, pricing, and all response statuses. Use the UI
package with the existing OpenAPI generator. Replace the obsolete weather
request in Confera.Api.http with a booking example and usable instructions.

## 6. Verification

Add tests alongside implementation rather than postponing them until the end.

### Domain and Application

- Existing period, service, pricing, and snapshot behavior stays correct.
- Expected errors carry the intended codes; configuration failures stay internal.
- Absent/deleted rooms are rejected.
- Time is read after lock acquisition; a start that passed while waiting fails.
- Persistence/commit failure does not return success.

Use simple test implementations of the persistence contracts and a controlled
TimeProvider rather than another mocking or clock package.

### Real PostgreSQL

| Scenario | Expected result |
| --- | --- |
| Room A, 11:00-15:00, Projector and Wi-Fi | 9400 UAH and complete snapshots. |
| Concurrent overlapping bookings | Exactly one success and one conflict. |
| Adjacent bookings | Both succeed. |
| Different rooms at the same time | Both succeed. |
| Rate/service edits committed before the lock is released | Booking uses fresh data. |
| Soft deletion committed before the lock is released | Booking is rejected. |
| Overlap during persistence | Constraint maps to the application conflict. |
| Failure after aggregate writes begin, before commit | No partial booking or children. |
| Cancellation while waiting | The operation ends and a later operation can obtain the lock. |

Use independent connections and deterministic coordination rather than timing
sleeps. Simulated room changes in these tests do not implement R1. Reuse the
existing isolated PostgreSQL fixture, real migrations, and fresh databases.

### HTTP

Use WebApplicationFactory and the existing PostgreSQL fixture to verify:

- Successful POST and actual stored data.
- Equivalent Z/explicit-offset inputs.
- Missing offset and excessive precision rejection.
- Required fields and serviceIds semantics.
- 400/404/409 responses and safe controlled-failure responses.
- Concurrent POSTs producing 201 and 409.
- The endpoint and contracts appearing in OpenAPI.

Do not duplicate the full Domain pricing matrix through HTTP.

## 7. Dependencies, CI, and documentation

Centrally pin compatible versions of Swagger UI and
Microsoft.AspNetCore.Mvc.Testing. Add the existing TRX reporter to Application
tests. Add direct project references when types are used directly. Retain
current .NET 10 and provider baselines.

Add Application.Tests to the current CI workflow, reusing its Linux job and
test-result artifacts. No new infrastructure matrix.

Update README for the first business operation, Swagger, and example usage;
booking decisions for locking/time/tariff reads; local-development and
CONTRIBUTING commands now that Application.Tests is implemented; the roadmap
for actual B1 progress and H1's partial delivery. Record that A1 must return
service IDs/names/prices, and that R1 follows the common locking protocol.
Register every new document in Confera.slnx. Preserve historical P1 evidence
and the original assignment.

## 8. Execution order and completion

1. Prepare the branch and save the plan.
2. Implement Domain validation and Application contracts.
3. Implement persistence and the transactional use case.
4. Verify the complete path and concurrency against PostgreSQL.
5. Add the controller, DTOs, errors, and HTTP tests.
6. Add Swagger UI, examples, CI, and documentation.
7. Perform final validation and review.

Final checks: restore, Release build, Domain.Tests, Application.Tests,
Integration.Tests, existing AppHost.Tests, no unexpected EF model changes,
Swagger UI and an example POST, documentation links, solution items,
git diff --check, and full diff review. Run relevant existing suites without
repeating unrelated infrastructure experiments.

B1 becomes Verified only when the endpoint works, snapshots/prices are correct,
concurrent requests are handled correctly, and the relevant checks pass.
Record actual commands/results and limitations. Done requires a confirmed merge;
publication and PR creation remain separate user requests. H1 remains incomplete
until its other four operations are implemented and verified.

Suggested implementation commit subject and PR title:
`feat(booking): implement transactional booking creation API`.

## 9. Implementation and validation record

Implemented on `feature/booking-creation`, based on fetched `origin/main`
at `d4cf369`. P1 commit `8875883` was confirmed as an ancestor. The branch has
no upstream and has not been published; implementation remains unmerged.

The delivered code follows the approved scope: shared typed Domain validation,
CreateBookingService, the focused booking transaction contract and PostgreSQL
implementation, POST /bookings, safe ProblemDetails, OpenAPI, Swagger UI,
examples, and Application tests in CI. No tables or migrations were added.
The agreed no-retry/no-idempotency policy remains in effect.

### Commands and results

Final validation was run on Windows with .NET SDK 10.0.401 and Docker Desktop's
Linux engine using PostgreSQL 18.6. The local date was 2026-09-16; the TRX
filenames record 2026-09-15 UTC. Test outputs are under the ignored
`artifacts/tests/b1-final/` directory.

```powershell
dotnet restore Confera.slnx
dotnet tool restore
dotnet build Confera.slnx --configuration Release --no-restore
$env:ConnectionStrings__confera = 'Host=localhost;Database=design_time_only'
dotnet ef migrations has-pending-model-changes --project src/Confera.Infrastructure --configuration Release --no-build
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/b1-final/domain
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/b1-final/application
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/b1-final/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/b1-final/apphost
git diff --check
```

The design-time connection variable was scoped to the EF command's process;
it is not a local application configuration change.

| Check | Result |
| --- | --- |
| Package and tool restore | Passed. |
| Release build | Passed; zero warnings and errors. |
| EF model/migrations | No pending model changes; existing schema retained. |
| Domain.Tests | 90 passed; no failures or skips. |
| Application.Tests | 17 passed; no failures or skips. |
| Integration.Tests | 48 passed; no failures or skips, including 12 booking transaction tests and 22 HTTP tests. |
| AppHost.Tests | 12 passed; no failures or skips, including startup/seed gating, recreation, worker retries, cancellation, and deadline checks. |
| Documentation | Local links/anchors, solution paths, and all Markdown registrations verified. |
| Diff | Changed/new source, dependencies, CI, and documentation reviewed; whitespace check passed. |

All 167 tests passed. B1 is Verified on 2026-09-16 and awaits publication and
merge; H1 remains In progress with one of five business operations delivered.

The booking checks verify the 9400 UAH example and complete stored snapshots,
overlap/adjacency/different-room behavior on independent connections, current
rate/services/deletion after a real lock wait, clock validation after waiting,
rollback after SaveChanges and before commit, exact exclusion-constraint
translation, and cancellation. HTTP checks exercise the actual API pipeline
and PostgreSQL, including original timestamp precision, error safety, a real
command timeout, and concurrent 201/409 responses. OpenAPI contracts and the
Swagger UI HTML route were checked through HTTP; no manual browser visual
review is claimed.

The first timeout check exposed Npgsql's non-retrying execution strategy
wrapping transient query errors in InvalidOperationException. Translation now
unwraps the specific EF/provider wrappers and recognizes the underlying
temporary failure. Both the controlled provider failure and an actual timed-out
PostgreSQL command return 503 after one attempt. Unexpected configuration and
schema failures remain safe 500 responses.

CI now runs Application.Tests alongside the existing suites. The hosted Linux
Actions run awaits publication; B1 did not repeat P1's separate local Linux
acceptance matrix. Room lifecycle operations, availability search, reports,
and the other four H1 operations remain subsequent work. R1's shared locking
contract and A1's service-discovery fields are recorded in the roadmap.
