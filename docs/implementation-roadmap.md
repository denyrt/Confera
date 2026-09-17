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
| Q1 | Reports and assignment completion | In progress | [Approved plan](plans/q1-reports-plan.md), 2026-09-17. Branch and documentation preparation complete; implementation and acceptance follow. |

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

## Record each task result

Update the task's status and delivery evidence, then add concise result notes
under its section when needed:

- Concrete behavior delivered and any remaining part of the original task.
- Actual validation results and material unavailable or failed checks.
- PR or commit reference when available; indicate an unmerged implementation.
- Approved changes to task scope or newly discovered open decisions.

Keep the roadmap at task level. Do not copy every code change or business rule
into it. Follow CONTRIBUTING.md for updates before and after merge.
