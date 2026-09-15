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

## Delivery sequence

The order below is the current implementation direction. Task boundaries may be
adjusted in an approved plan. Business API endpoints and HTTP tests can be added
alongside their corresponding application use cases.

| ID | Task | Status | Delivery and evidence |
| --- | --- | --- | --- |
| D1 | Domain booking creation and tariff pricing | Done | [PR #4](https://github.com/denyrt/Confera/pull/4), merged as `9539c15` on 2026-09-14; Release build without warnings and 70 passing domain tests recorded during review. |
| W1 | Implementation roadmap and shared maintenance process | Done | [PR #5](https://github.com/denyrt/Confera/pull/5), merged as `c5c031e` on 2026-09-15; Release build, documentation links, solution items, and diff checks passed. |
| P1 | PostgreSQL persistence and infrastructure foundation | Verified | Local `feature/postgres-persistence`, 2026-09-15: full acceptance matrix passed; Release builds without warnings, 88 Domain + 14 PostgreSQL + 12 AppHost/worker tests passed on both Windows and Linux. [Commands and evidence](p1-validation.md). Not merged. |
| B1 | Transactional booking application use case | Planned | Depends on P1. |
| R1 | Room management and lifecycle restrictions | Planned | Depends on P1; shares the room-locking protocol with B1. |
| A1 | Availability search | Planned | Depends on P1; reuses domain period and tariff validation. |
| H1 | Complete the five business API operations | Planned | Built alongside B1, R1, and A1. |
| Q1 | Reports and assignment completion | Planned | Depends on persistence and completed business operations. |

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

The [P1 specification](p1-persistence-specification.md) is the detailed acceptance
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
implementation validation is recorded in [P1 evidence](p1-validation.md).
Publication of implementation changes is a separate user request.

Implementation verified on 2026-09-15: all P1 acceptance rows passed on Windows
and Linux, including independent concurrent SQL writes, seed rollback/replay,
real worker retry/deadline/failure gating, first startup, container recreation,
Unicode scalar conformance and local/test coexistence. Each environment passed
114 implemented tests with no skips. Release builds had no warnings/errors;
EF found no pending model changes. Application.Tests still reports zero tests
and MTP exit 8, as required until B1. Local startup/EF commands, documentation
links, solution paths and diff checks passed. CI's Linux sequence passed locally;
a hosted Actions run and merge await publication. See the complete
[P1 validation record](p1-validation.md); this establishes Verified, not Done.

Subsequent NameIdentity refinement on the same date adds explicit null guards
and UTF-16 span appending without per-Rune boxing/string conversion. Windows
regression checks passed: 90 Domain tests and full PostgreSQL Unicode key
conformance; details and commands are appended to the P1 validation record.

Planning validation on 2026-09-15: tools/package restore succeeded; Release
build passed with no warnings or errors; all 70 existing Domain tests passed;
36 local documentation links/anchors, 27 solution paths, documentation
registration, and diff whitespace checks passed. No runtime behavior changed.

### B1: Transactional booking application use case

Completion criteria:

- Application coordinates fresh room/service reads, period and availability
  checks, domain booking creation, and atomic persistence.
- The agreed room-row locking and exclusion-constraint protocol prevents
  concurrent overlaps while allowing adjacent bookings.
- Integration tests use independent database connections to verify concurrent
  requests, conflict handling, rollback, and complete snapshot persistence.

### R1: Room management and lifecycle restrictions

Completion criteria:

- Create, edit, and soft-delete operations follow the agreed room policies.
- Active room-name uniqueness is enforced in persistence.
- Capacity reduction and deletion are rejected when ongoing or future bookings
  exist, using the shared transaction and locking protocol.
- Service editing, including renaming behavior, is explicitly agreed and
  preserves existing booking snapshots.
- Tests verify lifecycle rules and races with booking creation.

### A1: Availability search

Completion criteria:

- Search applies active-room, capacity, overlap, and full tariff-coverage rules.
- Search and booking use the same domain period and tariff interpretation.
- Queries avoid loading full booking histories or issuing a query per room.
- Tests cover adjacent intervals, unavailable rooms, and tariff gaps.

### H1: Complete the five business API operations

Completion criteria:

- All five agreed operations call application use cases through validated DTOs.
- Timestamp input requires an explicit offset and is normalized to UTC.
- Stable error responses do not expose internal database or exception details.
- OpenAPI describes the implemented contracts, and HTTP integration tests cover
  success, invalid input, not-found cases, and business conflicts.

### Q1: Reports and assignment completion

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
