# R1: Room management and optimistic concurrency

Approved by the maintainer on 2026-09-16, including GET /rooms/{id} and
If-Match for PUT and deletion of an active room. This is the execution contract;
delivery status and evidence belong in the roadmap and the record below.

## Scope and decisions

- POST /rooms creates a room and its services; returns 201 with their IDs.
- GET /rooms/{id} returns the current active room and a strong ETag; absent or
  deleted rooms return 404. This supporting read expands the original five
  operations to make conditional editing usable, including seeded rooms.
- PUT /rooms/{id} replaces all editable fields and returns 200 with current
  data. Name, capacity, hourlyRate and services are required; null/missing fields
  are invalid; an empty services array removes every current offering.
- Services match by the existing normalized name key. Matching names retain
  their IDs and accept new display spelling/prices; a different key creates a
  new offering with a new ID. Omitted services are physically removed. Historical
  service/rental snapshots never change.
- DELETE /rooms/{id} soft-deletes, retaining current services and history.
  An unknown ID returns 404. An already deleted room returns 204 without a
  version check, including repeats with an old or missing If-Match. A malformed
  header is still invalid. No restore operation is included.
- Capacity reduction and deletion require no booking with EndsAtUtc > nowUtc.
  Equality is completed. Other edits, including service removal, are allowed
  with future bookings; recorded services remain obligations of those bookings.
- No authentication, shared service catalog, automatic write retries, idempotency
  keys, PATCH, generic repositories, outbox or new infrastructure matrix.

## Version and HTTP preconditions

Room owns a nonempty UUID Version. Real changes to its fields, services or deleted
state rotate it; no-op replacements and booking creation do not. A migration
backfills existing rows with distinct generated UUIDs. Infrastructure maps Version
as an EF concurrency token. Supported room writes share B1's room-row lock;
arbitrary SQL service writes do not automatically participate in this protocol.

GET returns a deterministic JSON representation with services ordered by ID and
an opaque quoted strong ETag containing a representation-format prefix and the
version. Read the room, services and version in one SQL statement. Use no-store
for this editing read; conditional caching is not a new feature.

PUT and DELETE of an active room require explicit version tags in If-Match.
Accept the standard list syntax and match any strong tag. Weak tags never match;
syntactically valid unknown tags produce 412. Reject wildcard * as invalid_request
because it bypasses the agreed version protection. Missing preconditions produce
428; malformed headers produce 400. Request-independent validation precedes the
database; absent/deleted rooms return 404 for GET/PUT. Lifecycle conflicts are
checked before evaluating a supplied version, following HTTP precondition ordering.
No blind retry or automatic merge occurs after 412.

POST returns Location pointing to the implemented GET. PUT returns current data
without a validator header because the server normalizes names and generates
service IDs; retrieve the next ETag with GET (RFC 9110 section 9.3.4).

Errors use the existing ProblemDetails shape, code and traceId:

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | invalid_request | JSON, required fields, IDs or unsupported/malformed If-Match. |
| 400 | invalid_room_data | Invalid room fields or service set. |
| 404 | room_not_found | Unknown room, or deleted room for read/update. |
| 409 | room_name_conflict | An active room already uses the normalized name. |
| 409 | room_has_unfinished_bookings | Capacity reduction/deletion would affect reservations. |
| 412 | room_version_mismatch | No supplied strong tag matches the current version. |
| 428 | room_precondition_required | Supply If-Match for this active-room mutation. |
| 503 | room_persistence_unavailable | Temporary persistence failure; commit outcome may be unknown. |
| 500 | internal_error | Unexpected internal failure, without provider details. |

## Implementation order

1. Verify main prerequisites and create feature/room-management; record this plan.
2. Add validated atomic Domain updates, service spelling changes, soft deletion
   and version behavior without introducing booking history into Room.
3. Add focused Application room operations/contracts and Infrastructure storage.
   Create is atomic; updates/deletion use a fresh context and Read Committed
   transaction, lock the parent before reading current state, then check lifecycle
   and preconditions, save and commit. Use one floored TimeProvider UTC value after
   lock acquisition for the unfinished-booking existence query. Retain the lock
   through commit; pass cancellation to commands and reuse configured timeouts.
4. Add the version migration, HTTP contracts, preconditions, safe errors and DI.
5. Add behavioral Domain/Application tests and real PostgreSQL/HTTP scenarios:
   names, service identity/spelling, snapshots, lifecycle boundaries, stale/no-op
   writes, migrations, rollback, cancellation and races with real B1 operations.
6. Update README, rules, domain/status documentation and HTTP examples. Register
   this document in Confera.slnx. Build Release; run all four suites; check EF
   model consistency, OpenAPI, links, solution paths and the full diff.

No publication, commit, PR or merge is authorized by local implementation approval.
R1 can become Verified after checks; Done requires confirmed merge. H1 remains
in progress until A1 supplies availability search. Q1 remains subsequent work.

## Validation record

Verified on 2026-09-16 on Windows with Docker Desktop Linux containers and
PostgreSQL 18.6. Implementation started from fetched origin/main at ed7b528 on
feature/room-management, created with --no-track. Changes are local and unmerged.

| Check | Result |
| --- | --- |
| Tool/package restore | Passed. |
| Release build | Passed; zero warnings and errors. |
| Domain | 101 passed, zero skipped. |
| Application | 27 passed, zero skipped. |
| PostgreSQL/HTTP integration | 85 passed, zero skipped. |
| AppHost/worker | 12 passed, zero skipped. |
| EF model consistency | No pending model changes. |
| Documentation/solution | 71 local links/anchors and 35 solution paths valid; all 14 Markdown documents registered. |
| Diff whitespace | Passed. |

All 225 tests passed. The new scenarios cover full replacement and service ID
semantics, snapshots, completed/ongoing/future lifecycle boundaries, coherent
GET/ETag, stale/no-op writes, strong/list/repeated header handling, concurrent
HTTP updates and name conflicts, safe failures without replay, cancellation,
rollback after SaveChanges, upgrade of existing rooms and EF concurrency.
Controlled independent-connection races exercise real B1 and R1 operations in
both orders for deletion, capacity, rate and services. OpenAPI checks verify all
room operations, response statuses and If-Match parameters. The full AppHost
suite verifies migration/seed gating, repeat startup, failure and deadline paths.

Final build and suite commands:

```powershell
dotnet build Confera.slnx --configuration Release --no-restore
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/r1/domain
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/r1/application
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/r1/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/r1/apphost
```

The final TRX reports have UTC filename timestamps 14:36:57 (Domain/Application),
14:37:11 (Integration) and 14:40:29 (AppHost), dated 2026-09-16. They are stored
under the ignored artifacts/tests/r1 directories above. Tool restore and solution
restore passed; the EF check used:

```powershell
$env:ConnectionStrings__confera = 'Host=localhost;Database=design_time_only'
dotnet ef migrations has-pending-model-changes --project src/Confera.Infrastructure --configuration Release --no-build
```

R1 is Verified locally. No hosted Linux CI run, commit, push, PR or merge is
claimed. H1 has four of its five core operations verified; A1 availability and
Q1 reports remain separate work.
