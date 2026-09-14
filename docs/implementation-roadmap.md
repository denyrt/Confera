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
| P1 | PostgreSQL persistence and infrastructure foundation | Planned | Next implementation task; open decisions are listed below. |
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
record in the delivery table. Application and Integration test projects remain
empty; CI, availability checks, and concurrent-booking protection are not part of
this completed domain task. Room lifecycle changes remain under R1.

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

Open decisions to clarify before implementation:

- Monetary column precision, maximum accepted values, and multiplier precision.
- Timestamp precision at the .NET/PostgreSQL boundary.
- Migration execution through a separate runner or explicit command.
- Inclusion of room soft-delete state in the initial schema.
- Whether to retain a `Room.Bookings` navigation while keeping Booking an
  independent aggregate; the current implementation has no such navigation.
- Test database isolation and fixture lifetime, plus the PostgreSQL image version
  used consistently for local development and integration tests.

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
- Initial rooms, services, and tariffs follow the assignment and decision docs.
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
