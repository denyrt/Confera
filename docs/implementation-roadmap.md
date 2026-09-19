# Implementation roadmap

This document records Confera's delivery sequence, task status, completion
criteria, and validation evidence. Update the relevant entries as work advances,
whether the implementation is written by a person or an agent.

Detailed business rules remain in [the domain model](domain-model.md) and
[booking and pricing decisions](booking-and-pricing-rules.md). Repository
workflow and roadmap maintenance are defined in
[CONTRIBUTING.md](../CONTRIBUTING.md). A planned task still requires analysis,
clarification of open decisions, and approval before execution under
[the agent workflow guide](agent-workflow.md).

## Status definitions

| Status | Meaning |
| --- | --- |
| Planned | The task is identified but implementation has not started. |
| In progress | Work has started under an approved plan; completion criteria are not yet verified. |
| Verified | The task meets its completion criteria and relevant checks passed, but the implementation is not yet merged into `main`. |
| Done | The implementation is merged into `main`, completion criteria are met, and relevant checks passed. |

Code written, a successful build alone, or an open PR does not establish Done.
Record the actual checks and material limitations. If a completed task regresses,
record the regression and the follow-up task rather than preserving a misleading
completion claim.

P1 has one maintainer-accepted delivery exception: its verified implementation
was pushed directly to `main`, without a PR, and the maintainer requested that
it be recorded as Done after confirming the commit on `origin/main`. Its entry
records that route explicitly; it is not a PR merge and does not change the
GitHub Flow requirement for subsequent work.

## Delivery sequence

The order below is the current implementation direction. Task boundaries may be
adjusted in an approved plan. Business API endpoints and HTTP tests can be added
alongside their corresponding application use cases.

| ID | Task | Status | Delivery and evidence |
| --- | --- | --- | --- |
| D1 | Domain booking creation and tariff pricing | Done | [PR #4](https://github.com/denyrt/Confera/pull/4), merged as `9539c15` on 2026-09-14; Release build without warnings and 70 passing domain tests recorded during review. |
| W1 | Implementation roadmap and shared maintenance process | Done | [PR #5](https://github.com/denyrt/Confera/pull/5), merged as `c5c031e` on 2026-09-15; Release build, documentation links, solution items, and diff checks passed. |
| P1 | PostgreSQL persistence and infrastructure foundation | Done | Commit [`8875883`](https://github.com/denyrt/Confera/commit/8875883c69162bbf9c9ac7c304e2a3591dc05a8f) confirmed on fetched `origin/main`, 2026-09-15; direct push without PR, accepted by the maintainer. Full Windows/Linux acceptance matrix passed, with subsequent NameIdentity regressions recorded in [commands and evidence](plans/p1-validation.md). |
| B1 | Transactional booking application use case | Done | [PR #8](https://github.com/denyrt/Confera/pull/8), merged as `1e6b96e` on 2026-09-16 and confirmed on fetched origin/main. POST /bookings, HTTP tests, and Swagger UI; Release build and all 167 tests passed. [Plan and validation evidence](plans/b1-booking-implementation-plan.md#9-implementation-and-validation-record). |
| R1 | Room management and lifecycle restrictions | Done | [PR #9](https://github.com/denyrt/Confera/pull/9), merged as `5ce2eb6` on 2026-09-16 and confirmed on fetched origin/main. Room API, lifecycle guards and ETag/If-Match; Release build and all 225 tests passed on Windows. [Plan and evidence](plans/r1-room-management-plan.md#validation-record). |
| A1 | Availability search | Done | [PR #10](https://github.com/denyrt/Confera/pull/10), merged as `85988e8` on 2026-09-16 and confirmed on fetched origin/main. GET /rooms/availability with current services and page/pageSize; shared RentalPeriod and tariff coverage. Release build and all 290 tests passed on Windows. [Plan and validation evidence](plans/a1-availability-search-plan.md#8-implementation-and-validation-record). |
| H1 | Complete the five business API operations | Done | B1, R1, and A1 deliver all five core operations, plus the supporting room GET. A1's merge in PR #10 (`85988e8`) completes the operation set on main; HTTP/OpenAPI and all 290 tests passed. |
| Q1 | Reports and assignment completion | Done | Both reports, snapshot aggregates, HTTP/OpenAPI, real-seed assignment path, and documentation merged in [PR #11](https://github.com/denyrt/Confera/pull/11) (`b7928a8`), 2026-09-17. Release build and all 320 tests passed. [Plan and evidence](plans/q1-reports-plan.md#9-implementation-and-validation-record). |
| RF1 | Readability and explicit operation results | Done | Typed Application results and explicit controller mappings merged in [PR #12](https://github.com/denyrt/Confera/pull/12) as `f777e46`, confirmed on fetched origin/main on 2026-09-18. Release build and all 344 tests passed on Windows. Domain validation remains a later checkpoint. [Plan and validation](plans/readability-refactoring-plan.md#11-implementation-and-validation-record). |
| RF2 | Expected Domain validation failures without exceptions | Verified | Domain-owned typed failures and Try methods preserve RF1 Application/HTTP contracts. Release build and all 400 tests passed on Windows, 2026-09-18; EF model unchanged. Local implementation is unmerged. [Plan and validation](plans/domain-validation-results-plan.md#validation-record). |

### D1: Domain booking creation and tariff pricing

Completed behavior:

- Room creates a complete booking with service and rental-price snapshots.
- Booking generates its identity before creating its children, enforces segment
  coverage, and calculates total price from recorded values.
- Pricing covers the full UTC interval, handles midnight crossings, selects
  priorities, and rounds segments according to the decision document.
- Equal-priority daily overlaps are rejected; disjoint and adjacent rules are
  allowed. Rounded zero segment prices and totals are accepted.
- Domain behavior is tested; agent workflow and code readability guidance are
  documented.

Evidence: [domain tests](../tests/Confera.Domain.Tests/), PR #4 and the validation
record in the delivery table. At D1 delivery, Application and Integration tests were
empty; CI, availability checks, and concurrent-booking protection were not part of
this completed domain task. Room lifecycle changes remain under R1.

P1's approved specification changes numerical bounds, time precision, name
comparison, and tariff priorities. The equal-priority statement above records
D1's delivered behavior. P1 now enforces globally unique priorities, including
disjoint rules, as recorded in its separate validation evidence.

### W1: Implementation roadmap and shared maintenance process

Completion criteria:

- The roadmap records the known completed domain work, upcoming tasks, and
  unresolved decisions without claiming that planned behavior is implemented.
- CONTRIBUTING.md defines updates before and after merge for human and agent
  authors, including the narrow exception for a final status update directly on
  `main`; agent instructions require reading and maintaining the roadmap.
- README links to the roadmap, which is registered under `/docs/` in Confera.slnx.
- Documentation links and solution item paths are valid, the diff is clean, and
  the solution builds. The documentation change is merged before marking W1 Done.

Validation on 2026-09-15: the Release build passed with no warnings or errors;
diff whitespace checks, 22 local documentation links, and solution item paths
passed verification. No application behavior changed. PR #5 was merged as
`c5c031e` on 2026-09-15; merge into `origin/main` was confirmed, and documentation
links and solution item paths were verified again when recording completion.

### P1: PostgreSQL persistence and infrastructure foundation

Completion criteria:

- Infrastructure owns the EF Core context, explicit entity mappings, and
  versioned migrations, respecting the documented dependency boundaries.
- A fresh PostgreSQL database can be created from migrations. The schema
  preserves booking ownership, snapshots, and agreed data-integrity constraints.
- AppHost starts PostgreSQL and the API with the agreed connection settings,
  readiness dependencies, and local persistence behavior.
- The agreed migration and initial-data mechanism supports repeated startup
  without duplicate seed data or unintended data loss.
- Testcontainers supplies an isolated PostgreSQL environment, applies the real
  migrations, and verifies booking round trips and database constraints.
- CI restores and builds the solution, runs the implemented test suites, and
  preserves test results and failure diagnostics.
- Local startup and test commands are documented and verified.

The [P1 specification](plans/p1-persistence-specification.md) is the detailed acceptance
contract and ordered implementation plan. It includes required Domain updates,
SQL integrity/concurrency checks, first-start/repeat-start worker behavior,
atomic seed, container recreation, and test isolation. All criteria must pass
before P1 is Verified; a build alone is insufficient.

Decisions resolved during planning on 2026-09-15:

- Inclusive numerical bounds and explicit `numeric` range/precision checks;
  multiplier precision is two digits and money precision is three.
- One-microsecond UTC precision; reject finer external inputs before pricing
  and canonicalize system clock values before Domain processing.
- Dedicated MigrationWorker with bounded provider retries, success gating for
  API, and one-time transactional demo initialization outside schema migrations.
- Minimal `Room.IsDeleted` in the initial schema, with normalized active-name
  uniqueness; room lifecycle operations remain R1.
- Shared specified name normalization; Room owns current services and retains
  no booking-history navigation. Booking remains an independent aggregate.
- Globally unique tariff Priority replaces equal-priority interval constraints;
  booking overlap protection remains a PostgreSQL exclusion constraint.
- Consistent `postgres:18.6`, persistent local development volume with the
  PostgreSQL 18 mount path, temporary test containers, and a fresh database per
  test. AppHost tests never use the local development volume.

No business or architecture decision from P1 planning remains open. Compatible
package patch versions and routine implementation details are selected and
verified within the specification. New material incompatibilities must be
surfaced rather than silently changing this contract.

Planning result: the specification and ordered implementation plan are prepared;
that planning result alone did not establish persistence delivery. The user has
since approved the specification and its ordered execution plan. Actual P1
implementation validation is recorded in [P1 evidence](plans/p1-validation.md).
Publication of implementation changes is a separate user request.

Implementation verified on 2026-09-15: all P1 acceptance rows passed on Windows
and Linux, including independent concurrent SQL writes, seed rollback/replay,
real worker retry/deadline/failure gating, first startup, container recreation,
Unicode scalar conformance and local/test coexistence. Each environment passed
114 implemented tests with no skips. Release builds had no warnings/errors;
EF found no pending model changes. Application.Tests still reports zero tests
and MTP exit 8, as required until B1. Local startup/EF commands, documentation
links, solution paths and diff checks passed. CI's Linux sequence passed locally;
a hosted Actions run and merge awaited publication at that initial handoff.
See the complete [P1 validation record](plans/p1-validation.md); those local checks
established Verified before publication.

Subsequent NameIdentity refinement on the same date adds explicit null guards
and UTF-16 span appending without per-Rune boxing/string conversion. Windows
regression checks passed: 90 Domain tests and full PostgreSQL Unicode key
conformance; details and commands are appended to the P1 validation record.

Completion recorded on 2026-09-15 at the maintainer's request: after
`git fetch origin`, `origin/main` resolves to
`8875883c69162bbf9c9ac7c304e2a3591dc05a8f`, containing the P1 implementation and
NameIdentity follow-up. Delivery was a direct push, with no PR or PR merge.
The local feature branch had incorrectly tracked `origin/main`; CONTRIBUTING
now requires `--no-track` when branching from `origin/main`, an explicit first
push destination, and verification of the matching feature upstream. Hosted
Actions results were not verified in this status update; the existing local
Windows/Linux validation remains the recorded test evidence. No application
code or P1 behavior changed in this documentation update.

Planning validation on 2026-09-15: tools/package restore succeeded; Release
build passed with no warnings or errors; all 70 existing Domain tests passed;
36 local documentation links/anchors, 27 solution paths, documentation
registration, and diff whitespace checks passed. No runtime behavior changed.

### B1: Transactional booking application use case

The maintainer approved the [complete B1 plan](plans/b1-booking-implementation-plan.md)
on 2026-09-16. Scope includes the booking portion of H1 and Swagger UI, with no
automatic booking retries, idempotency keys, or new schema.

Completion criteria:

- Application coordinates fresh room/service reads, period and availability
  checks, domain booking creation, and atomic persistence.
- The agreed room-row locking and exclusion-constraint protocol prevents
  concurrent overlaps while allowing adjacent bookings.
- Integration tests use independent database connections to verify concurrent
  requests, conflict handling, rollback, and complete snapshot persistence.
- POST /bookings exposes the agreed request, price breakdown, and safe error
  contracts, with HTTP tests, OpenAPI, and development Swagger UI.

Verified on 2026-09-16: the complete booking path persists the expected 9400 UAH
example and snapshots. Independent-connection tests cover overlaps, adjacency,
current room/service data after locking, rollback, cancellation, and safe errors.
All 167 tests passed on Windows (90 Domain, 17 Application, 48 Integration,
12 AppHost), without skips. Restore, Release build with zero warnings/errors,
EF model check, OpenAPI/Swagger HTTP checks, documentation links/solution items,
and diff checks passed. Application.Tests is now part of CI. See the
[command and evidence record](plans/b1-booking-implementation-plan.md#9-implementation-and-validation-record).

Completion recorded on 2026-09-16 at the maintainer's request: after
`git fetch origin`, both local `main` and `origin/main` resolve to
`1e6b96e396d8473209a75e5f3a7e3cb491306940`, the squash merge of
[PR #8](https://github.com/denyrt/Confera/pull/8). The saved TRX reports confirm
all 167 local tests passed. Documentation links, solution paths, and diff checks
passed for this status update; application behavior did not change. Hosted
Linux Actions results were not verified in this update. The B1 plan preserves
the earlier pre-merge validation record; this roadmap records final delivery.
R1, A1, reports, and the remaining H1 operations are outside this delivery.

### R1: Room management and lifecycle restrictions

The maintainer approved the [R1 execution plan](plans/r1-room-management-plan.md) on
2026-09-16. Scope includes its three H1 operations and a supporting GET /rooms/{id}
for conditional editing. Services match by normalized names; spelling updates
retain IDs, renamed offerings receive new IDs. PUT is a complete replacement.
An explicit UUID version protects PUT and active-room deletion through If-Match;
missing/stale conditions return 428/412. Repeated deletion remains 204.

Completion criteria:

- Create, edit, and soft-delete operations follow the agreed room policies.
- Active room-name uniqueness is enforced in persistence.
- Capacity reduction and deletion are rejected when ongoing or future bookings
  exist, using the shared transaction and locking protocol.
- Room and service mutations acquire the same room-row lock as B1 before
  reading current state, as described in the [booking rules](booking-and-pricing-rules.md#booking-transaction-and-room-changes).
- Service editing, including renaming behavior, is explicitly agreed and
  preserves existing booking snapshots.
- Tests verify lifecycle rules and races with booking creation.
- GET returns coherent current state and ETag; stale writes are rejected,
  service-only changes rotate the version, and no-op edits/bookings retain it.
- A version migration preserves existing data; all room HTTP contracts are
  documented and tested, including conditional requests and safe failures.

Verified on 2026-09-16: room and service changes follow the agreed identity,
snapshot and lifecycle rules; concurrent stale writes are rejected. Real B1/R1
races in both orders verify the shared locking protocol. Migration upgrade,
rollback, cancellation, safe errors and HTTP/OpenAPI contracts passed. All 225
tests passed on Windows (101 Domain, 27 Application, 85 Integration, 12 AppHost),
without skips. Restore, Release build with zero warnings/errors, EF model check,
71 local documentation links/anchors, 35 solution paths, document registration
and diff checks passed. See the [validation record](plans/r1-room-management-plan.md#validation-record).

Completion recorded on 2026-09-16 at the maintainer's request: after
`git fetch origin`, both local `main` and `origin/main` resolve to
`5ce2eb606a8d933e524068682e140e4f60b46aa6`, the squash merge of
[PR #9](https://github.com/denyrt/Confera/pull/9). The validation record above
documents the 225 passing local tests; application behavior did not change in
this status update. Hosted Linux CI results were not verified in this update.
The R1 plan preserves the pre-merge validation record; this roadmap records
final delivery. A1 and reports remain separate tasks.

### A1: Availability search

The maintainer approved the [A1 plan](plans/a1-availability-search-plan.md) on
2026-09-16, authorizing feature-branch creation and specification preparation
first. The maintainer subsequently authorized Domain unit 1, introduced
RentalPeriod in fb72fc2, and explicitly authorized unit 2 and all following
units. Implementation was verified on feature/availability-search and is now
merged into main through PR #10.

Agreed scope: GET /rooms/availability with start/end, minimum capacity, and
page/pageSize. Return current room data, base hourly rate, and services, without
a period-price estimate. Pagination exposes items, page, pageSize, and
hasNextPage, without totalCount; defaults are page 1 and pageSize 20, maximum
100. Missing tariff coverage yields a successful empty result. Search does not
reserve rooms and shares Domain period/tariff interpretation with booking.

Preparation validation on 2026-09-16: Release build passed with zero
warnings/errors; 80 local links/anchors, 36 solution paths, all 15 Markdown
registrations, and diff checks passed. Runtime tests were not run for the
documentation-only change; this is not A1 implementation acceptance evidence.

Completion criteria:

- Search applies active-room, capacity, overlap, and full tariff-coverage rules.
- Search and booking use the same domain period and tariff interpretation.
- Queries avoid loading full booking histories or issuing a query per room.
- Results include current service IDs, names, and prices so clients can select
  services when creating a booking.
- Database pagination uses capacity/ID ordering and correct hasNextPage behavior
  without a count query; room services do not consume page slots.
- HTTP query validation, safe errors, and OpenAPI follow the agreed contract.
- Tests cover adjacent intervals, unavailable rooms, tariff gaps, pagination,
  current offerings, and the search-to-booking conflict scenario.

Verified on 2026-09-16: the read-only search filters and pages rooms in PostgreSQL,
returns their current services in the same statement, and skips the room query
when tariffs do not cover the period. Shared timestamp parsing preserves B1
input rules. Real PostgreSQL/HTTP checks verify ordering, overlaps, query count,
current offerings, safe failures, cancellation, and booking conflicts after
search. All 290 tests passed on Windows (111 Domain, 45 Application, 122
Integration, 12 AppHost), without skips. Restore, Release build with zero
warnings/errors, EF model consistency, documentation/solution checks, and diff
checks passed. See the [validation record](plans/a1-availability-search-plan.md#8-implementation-and-validation-record).
That pre-merge validation did not include a hosted Linux CI run; Q1 remains
separate work.

Completion recorded on 2026-09-16 at the maintainer's request: after
`git fetch origin`, both local `main` and `origin/main` resolve to
`85988e86749e20c42032d3b086163ecb80fb2d5d`, the squash merge of
[PR #10](https://github.com/denyrt/Confera/pull/10). The saved TRX reports confirm
all 290 local tests passed. Documentation links, solution paths, and diff checks
passed for this status update; runtime tests were not rerun because application
behavior did not change. Hosted Linux CI results were not verified in this
update. The A1 plan preserves the pre-merge validation record; this roadmap
records final delivery.

### H1: Complete the five business API operations

The booking operation, its HTTP contract/tests, and development Swagger UI are
delivered with B1. R1 delivers room creation, editing and deletion, together with
its approved supporting read. A1 adds availability search. All five core
operations and their HTTP/OpenAPI contracts are verified by the complete test
run recorded under A1 above. A1's confirmed merge in PR #10 completes delivery
of the full operation set, so H1 is Done as of 2026-09-16. Reports remain Q1.

Completion criteria:

- All five agreed operations call application use cases through validated DTOs.
- Timestamp input requires an explicit offset and is normalized to UTC.
- Stable error responses do not expose internal database or exception details.
- OpenAPI describes the implemented contracts, and HTTP integration tests cover
  success, invalid input, not-found cases, and business conflicts.

### Q1: Reports and assignment completion

The maintainer approved the [Q1 plan](plans/q1-reports-plan.md) and its execution
on 2026-09-17. Scope is GET /reports/rooms and GET /reports/services with the
existing snapshot and reporting-period rules. Confirmed decisions: exact
case-sensitive service-name grouping, rooms with selected bookings only, all
groups without pagination, and decimal booked seconds with microsecond precision.
Unselected alternatives are documented as optional future work in the plan;
they are not remaining Q1 requirements. The broader readability/error-result
refactor is outside Q1.

Preparation used feature/booking-reports from fetched origin/main d851fdc,
which contains the completed B1/R1/A1/H1 prerequisites. Detailed task documents
now live under docs/plans with corresponding solution registration and rebased
links; previous plan contents and historical evidence were preserved.

Completion criteria:

- Both agreed reports use recorded prices and the documented reporting period
  semantics, include soft-deleted room history, and avoid join multiplication.
- Initial rooms, services, and tariffs delivered under P1 still follow the
  assignment and decision docs; Q1 verifies their end-to-end use.
- Documentation accurately describes the implemented scope and verified commands.
- All relevant implemented test suites pass; limitations are explicitly recorded.

Verified on 2026-09-17: both reports filter and aggregate recorded values in one
parameterized PostgreSQL statement each. Tests verify period boundaries, exact
decimal seconds and money, no join multiplication, full results, historical
prices/deleted rooms, name grouping and sorting, safe failures and cancellation.
The real demo seed -> search -> HTTP booking -> reports scenario produces
8600 UAH rental + 800 UAH services = 9400 UAH. All 320 tests passed on Windows
(111 Domain, 54 Application, 143 Integration, 12 AppHost), without skips. Tool
and package restore, Release build with zero warnings/errors, EF model check,
documentation links/solution folders, formatting and diff checks passed.
See the [validation record](plans/q1-reports-plan.md#9-implementation-and-validation-record).

Completed on 2026-09-17: [PR #11](https://github.com/denyrt/Confera/pull/11)
merged as `b7928a8`, confirmed on fetched `origin/main`. The merged tree matches
the version covered by the validation record above. Hosted Linux CI results
were not verified in this status update.

Reports intentionally return all groups without pagination and do not promise
a shared snapshot across separate requests.

### RF1: Readability and explicit operation results

The maintainer accepted the [refactoring plan](plans/readability-refactoring-plan.md)
on 2026-09-18 and authorized a feature branch with documentation as its first
changes and one local documentation commit (`4c2e1f5`). The maintainer then
authorized implementation of the agreed boundary refactor. The boundary work is
Done: [PR #12](https://github.com/denyrt/Confera/pull/12) merged as `f777e46`,
confirmed on fetched `origin/main` and matching local `main` on 2026-09-18.

Agreed direction: a small Application-owned result type, feature-specific typed
errors, explicit controller HTTP mapping, shared ProblemDetails formatting, and
a global handler for unexpected failures. Booking established the first example,
followed by room operations, availability, and reports. Known Domain/persistence
exceptions are adapted at the Application boundary during this stage. Automatic model
validation and all existing public contracts remain unchanged. Non-throwing
Domain validation is a later checkpoint, separate from boundary completion.

Completion criteria:

- Application exposes expected outcomes explicitly for all four feature areas;
  controllers visibly choose HTTP responses, including request-parsing failures.
- Expected errors leave the global handler while unexpected failures,
  cancellation, and technical diagnostics retain their intended behavior.
- Existing responses, OpenAPI, validation precedence, transaction/locking rules,
  snapshots, and no-retry behavior remain compatible and pass relevant tests.
- Current architecture documentation and actual validation evidence are updated;
  later readability work and any scope revisions are recorded explicitly.

Preparation began from fetched `origin/main` at `3f4c2f7`, which contains all
completed business-operation prerequisites. The plan records the preparation
checks, subsequent implementation authorization, and detailed runtime evidence.

Preparation verified on 2026-09-18: Release build passed with zero warnings and
errors; 109 local documentation links/anchors, 38 solution paths, all 17 Markdown
registrations, solution-folder placement, and diff checks passed. Runtime tests
were not run because preparation was documentation-only. This evidence does not
establish runtime acceptance. Implementation was authorized separately afterward.

Implementation verified on 2026-09-18: Release build passed with zero warnings
and errors; Domain 111, Application 69, Integration 152, and AppHost 12 tests
passed (344 total, no failures/skips in final runs). Existing HTTP/OpenAPI,
concurrency, snapshot, and cancellation coverage passed. Added tests cover result
invariants, transaction acquisition/disposal, mixed input precedence, original
technical diagnostics, request/trace correlation, and error cache headers.
The EF model has no pending changes. All 111 local links/anchors, 38 solution
paths, 17 Markdown registrations, formatting, and diff checks passed.

Initial AppHost attempts failed because of explicit Docker pipe overrides; all
12 passed with the documented normal Docker context discovery. The local
validation record does not include hosted Linux CI. Runtime evidence is recorded
in the plan and ignored TRX artifacts. Merge confirmation completes RF1;
non-throwing Domain validation remains separate. The plan retains its historical
pre-merge validation record; this roadmap records the final merged status.

### RF2: Expected Domain validation failures without exceptions

The maintainer approved the [RF2 plan](plans/domain-validation-results-plan.md)
and implementation on 2026-09-18. Work starts from RF1's completed baseline on
`refactor/domain-validation-results` with no upstream.

Completion criteria:

- Booking, availability, and room create/update use non-throwing Domain input
  validation with typed failures, without Domain-to-Application dependencies.
- Throwing wrappers share the same rules; configuration/invariant failures and
  cancellation remain distinct from expected input rejection.
- RF1 HTTP details, precedence, metadata, transactions, snapshots, and existing
  business rules remain compatible; new paths and all four suites are verified.
- Current architecture documentation and actual validation evidence are updated.

Verified on Windows, 2026-09-18: Release build passed without warnings/errors;
Domain 142, Application 85, Integration 161, and AppHost 12 tests passed (400
total, no failures/skips in final runs). Before refactoring, 12 Domain and nine
HTTP characterization cases passed on RF1; they retain exact room details and
mixed-input precedence after migration. New coverage protects typed failures,
wrappers, no partial mutations, input capture, cancellation, and cleanup.

EF reports no model changes. All 115 local links/anchors, 39 solution paths,
18 Markdown registrations, scoped formatting, source review, and whitespace
checks passed. Actual commands and the corrected test-only assertion from the
first Domain run are recorded in the plan. Hosted Linux CI was not run. RF2
remains local and unmerged; confirming merge and updating this entry to Done
remain the post-merge handoff.

## Record each task result

Update the task's status and delivery evidence, then add concise result notes
under its section when needed:

- Concrete behavior delivered and any remaining part of the original task.
- Actual validation results and material unavailable or failed checks.
- PR or commit reference when available; indicate an unmerged implementation.
- Approved changes to task scope or newly discovered open decisions.

Keep the roadmap at task level. Do not copy every code change or business rule
into it. Follow CONTRIBUTING.md for updates before and after merge.
