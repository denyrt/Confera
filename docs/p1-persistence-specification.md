# P1: PostgreSQL persistence specification

Status: implementation Done on 2026-09-15. Verified commit `8875883` is confirmed
on fetched `origin/main` after a direct push without a PR. The maintainer
accepted this delivery exception; see the roadmap for its record. The acceptance
contract and historical approved implementation plan below remain unchanged.
Prepared and approved for implementation on 2026-09-15. This document is the
complete acceptance contract for P1. See the [validation record](p1-validation.md)
and [roadmap](implementation-roadmap.md) for actual implementation evidence.

Read this with [repository conventions](../CONTRIBUTING.md), the
[agent workflow](agent-workflow.md), [domain model](domain-model.md),
[business decisions](booking-and-pricing-rules.md), and
[roadmap](implementation-roadmap.md). Preserve the original technical assignment.

## Outcome and scope

Deliver a working PostgreSQL persistence foundation: domain-compatible EF Core
mapping and migrations, database integrity constraints, one-shot migration and
demo-data worker, Aspire orchestration, isolated real-database tests, CI, and
verified development instructions. A first AppHost launch against a fresh
database must migrate and initialize it successfully without an AppHost restart.
Repeated launches must preserve existing data.

P1 includes the necessary changes to existing Domain validation and tests for
the agreed numerical bounds, microsecond precision, name comparison, globally
unique tariff priorities, and minimal room soft-delete state. These are approved
changes to the behavior originally delivered in D1.

P1 does not implement business endpoints, booking/availability application use
cases, reports, room lifecycle operations, or their transaction and locking
protocol. Those remain B1, R1, A1, H1, and Q1. Do not add a generic repository,
shared service catalog, tariff-management API, or speculative abstractions.

## Projects, dependencies, and configuration

- Infrastructure owns the context, explicit mappings, migrations, design-time
  context creation, provider configuration, and seed implementation.
- Domain remains free of EF Core, Npgsql, hosting, and ASP.NET Core references.
  Rehydrate existing aggregates through their private constructors/backing
  fields; do not expose setters or mutable collections solely for EF.
- Add `src/Confera.MigrationWorker`, an executable one-shot host referencing
  Infrastructure and ServiceDefaults. AppHost references Api and MigrationWorker.
  Worker orchestration does not put migrations in the worker project.
- Integration.Tests uses Infrastructure and directly references Domain when it
  uses domain types. Add a focused AppHost test project referencing AppHost if
  needed to verify real orchestration. Register projects and documents in
  `Confera.slnx`, and update CONTRIBUTING's actual dependency table on delivery.
- Use existing .NET 10, Aspire 13.5.3, Microsoft.Testing.Platform, and xUnit v3.
  Pin compatible stable EF Core 10, Npgsql provider, Aspire PostgreSQL hosting,
  and Testcontainers packages through `Directory.Packages.props`. Select and
  record exact compatible versions during implementation; no broad SDK upgrade,
  preview package, or `AddEFMigrations` dependency is part of P1.
- PostgreSQL is pinned to the latest stable release at planning time,
  **`postgres:18.6`**, identically for AppHost and all container-backed tests.
  Do not use `latest` or change this baseline silently on a later execution date.
  Use UTF-8 databases. Enable `btree_gist` through the schema migration.
- Use one documented logical connection name, `confera`, consistently across
  AppHost, API, worker, and configuration. Secrets come from Aspire/configuration
  or user secrets, never committed connection strings or machine-specific paths.
- API composes Infrastructure DI and database health checks; controllers remain
  independent of the context. Normal API startup never applies migrations or
  seeds data. Design-time migration commands must work without running AppHost.

## Domain and database value contract

All bounds below are inclusive. Validate user/configuration inputs before
calculating or mutating aggregates; invalid replacement input leaves the
existing aggregate unchanged. Database constraints protect persisted values
including writes made through SQL rather than EF.

| Value | Accepted range | Fractional digits |
| --- | --- | --- |
| Current or snapshotted room hourly rate, UAH | 1,000 to 100,000 | At most 3 |
| Current or snapshotted service price, UAH | 200 to 20,000 | At most 3 |
| Current or snapshotted tariff multiplier | 0.50 to 2.00 | At most 2 |
| Rounded segment price, UAH | 0 to 4,800,000 | At most 3 |
| Booking total, UAH | 0 to 999,999,999,999,999.999 | At most 3 |

The total bound is a technical storage/overflow guard, not a new commercial
booking limit. Selected services are charged once; there is no new service-count
limit. Rental before segment rounding is bounded by `24 * 100000 * 2`, but the
sum of individually rounded segments can differ from rounding the rental once.
Do not infer a smaller total bound from that formula.

Money uses `decimal` in .NET. Trailing decimal zeros do not count as additional
precision. Reject an extra significant fractional digit rather than rounding
input. Segment calculations retain the existing proportional-hour calculation
and round each segment to 3 digits with `MidpointRounding.AwayFromZero`. Preserve
all applicable tariff boundaries, even when a lower-priority rule is masked.
The total equals the sum of recorded segment and selected-service prices.
Small positive-duration segments may round to zero; zero segment prices and
zero totals remain representable. Input rate/service minimums do not apply to
calculated segment prices.

Use unconstrained PostgreSQL `numeric`, `NOT NULL`, and named checks such as
`value BETWEEN minimum AND maximum AND value = round(value, scale)` for these
columns. Do not use a fixed-scale `numeric(p,s)` typmod for validated inputs:
it can round before a CHECK evaluates, hiding an invalid input. Finite range
checks must also reject `NaN` and infinities. Verify this through direct SQL.

### Time precision

- Persist UTC instants as `timestamptz(6)` and daily UTC tariff hours as
  `time(6) without time zone`. The common precision is **1 microsecond**, or
  10 .NET ticks. Half-open `[start,end)` intervals remain unchanged.
- Reject externally supplied booking timestamps and tariff hours with
  `Ticks % 10 != 0` before validation/calculation; never truncate them after
  calculating a price. Domain entry points enforce this precision contract.
- Floor system-generated UTC clock values and booking creation timestamps to
  whole microseconds before domain processing. Use the same canonical current
  time throughout one operation. Do not round a clock into the future.
- Preserve UTC kind on timestamp round trips and reject non-UTC Domain input.
  Future HTTP parsing/explicit-offset handling remains H1.
- Disable Npgsql's implicit DateTime MinValue/MaxValue-to-infinity conversion
  before provider initialization. Persist finite values only, within the .NET
  DateTime range; with microsecond precision the upper endpoint is
  `9999-12-31T23:59:59.999999Z`. Database checks reject infinities and dates
  outside this range. Daily hours remain within `[00:00,24:00)`.
- Duration remains 30 minutes to 24 hours, inclusive, without a 30-minute step.
  A positive segment can be shorter than 30 minutes. Update 100-nanosecond edge
  tests to the new precision without weakening midnight/coverage tests.

PostgreSQL already stores only microseconds. Direct SQL containing more precise
timestamp text can be normalized by PostgreSQL's parser before a constraint
sees it; P1 guarantees rejection at .NET input boundaries and exact representable
database values, not recovery of precision already discarded by SQL parsing.

### Names and identity

Active room names are globally unique; current service names are unique within
their room. Both comparisons ignore case and surrounding whitespace. Keep the
display spelling, internal whitespace, accents, and alphabet intact. Do not
merge Latin and Cyrillic lookalikes, collapse internal spaces, remove accents,
or introduce NFC/NFKC normalization. Snapshot names remain historical display
values, not keys for current offerings.

The canonical key is the trimmed name mapped to Unicode simple uppercase using
PostgreSQL 18's built-in `pg_c_utf8` case mapping; compare keys ordinally using
the `C` collation. Simple mapping avoids multi-character case expansions.
Trim the same explicitly enumerated characters in .NET and SQL:
`U+0009–000D`, `U+0020`, `U+0085`, `U+00A0`, `U+1680`, `U+2000–200A`,
`U+2028`, `U+2029`, `U+202F`, `U+205F`, `U+3000`.

Infrastructure owns a schema function for this key and generated key columns;
direct SQL must not be able to supply a forged normalization key. Domain uses
one shared corresponding name helper for service matching and duplicate checks.
Start from invariant simple uppercase in .NET and verify provider agreement;
if Unicode/runtime mappings differ, add a documented compatibility mapping for
the PostgreSQL 18 baseline rather than accept inconsistent keys. A batched
conformance test comparing individual Unicode scalar case mappings can detect
differences without one database round trip per character. Include Ukrainian,
Latin, Greek, dotted/dotless I, sharp S, non-breaking spaces, and supplementary
characters in named regression examples. The previous `OrdinalIgnoreCase`
implementation is not assumed to be identical to this new contract.

Names and codes retain existing non-empty/64 UTF-16-code-unit Domain limits;
database checks must not admit a value that cannot be rehydrated under those
limits. In particular, PostgreSQL character count and .NET UTF-16 length differ
for supplementary characters. Keep non-empty UUIDs and positive capacities
consistent with existing Domain guards.

## Schema, ownership, and integrity

Use explicit mappings and stable, named constraints; let errors identify the
violated invariant. No database table for `BookingRentalSegment` is required.

| Entity/table | Ownership and essential constraints |
| --- | --- |
| Room | Application-generated UUID PK; name/key, positive capacity, hourly rate, `IsDeleted NOT NULL DEFAULT false`; unique active normalized name index filtered by `IsDeleted = false`. |
| RoomService | UUID PK and required Room FK; current name/key and price; unique `(RoomId, NameKey)`. |
| BookingPricingRule | UUID PK, non-empty code/name, daily start/end, multiplier, integer priority; start differs from end; globally `UNIQUE(Priority)`. |
| Booking | UUID PK, required Room FK; UTC start/end/created time, hourly-rate snapshot and total; finite timestamps and 30-minute–24-hour duration checks. |
| BookedRoomServiceSnapshot | UUID PK and required Booking FK; recorded service name and price; no FK to the current service. |
| BookingPriceSegment | UUID PK and required Booking FK; UTC start/end with positive duration; code, multiplier and hourly-rate snapshots, rounded price; no FK to a pricing rule. |
| Initialization marker | Unique seed key and completion metadata; records successful one-time demo initialization in the same transaction as its data. |

Room owns its current service collection; Booking remains a separate aggregate
and owns its snapshot/segment collections. Do not add `Room.Bookings` or load
history when loading a Room. Block physical room deletion while bookings refer
to it; service deletion must not delete historical snapshots. Define child FK
delete behavior explicitly, and support ordinary current-service replacement.
P1 adds soft-delete state to the initial schema only; lifecycle operations and
ongoing/future-booking guards remain R1.

Enforce booking overlaps with a PostgreSQL GiST exclusion constraint combining
Room ID equality and `tstzrange(start,end,'[)')` intersection. It must reject
overlapping writes on independent connections, including races, and allow
adjacent bookings and concurrent bookings in different rooms. Keep an index
supporting room/period lookups and child FKs; avoid speculative report indexes.

Tariff priority is **globally unique**, including disjoint/adjacent rules and
rules outside the requested booking. Higher priority wins; priorities need not
be consecutive or non-negative. Different-priority intervals may overlap and
cross midnight. Domain validates the whole set, and the database unique
constraint protects concurrent configuration writes. There is no auxiliary
daily-interval table, tariff overlap exclusion constraint, or synchronization
trigger. Booking's overlap exclusion constraint remains required.

Aggregate-wide segment coverage, total equality, and service ownership checks
remain Domain invariants used by the supported write path. Do not claim ordinary
row CHECK/FK constraints independently prove these cross-row invariants. Do not
add a booking-coverage trigger system to P1. Test complete aggregate persistence
and rollback; distinguish these guarantees from the SQL-enforced invariants.

## Migration worker and AppHost

Create one versioned initial migration, its model snapshot, and a design-time
factory or equivalent documented EF creation path. Real database tests use
`MigrateAsync`, not `EnsureCreated`, to exercise extensions, generated columns,
functions, and custom constraints. Keep schema migrations free of demo data.

AppHost order is PostgreSQL readiness, then MigrationWorker, then API after
successful worker completion. Configure `WaitForCompletion` or the compatible
Aspire 13.5.3 mechanism explicitly. Worker success (exit 0) means both requested
migrations and seed completed or legitimately skipped. Unhandled failure or
incomplete cancellation must produce a non-zero result and prevent API startup.
Log the failing phase and provider error safely, without credentials. A completed
worker stops itself; it is not a permanent polling service.

Enable Npgsql `EnableRetryOnFailure` for worker persistence. Use bounded
transient retries (at most 6 retries, maximum 5-second delay) and an overall
120-second worker database-operation deadline. Shutdown cancellation still
propagates. Do not endlessly retry authentication, invalid schema, or business
constraint errors. Do not install blanket API retries: B1's explicit
transactions will need replay of the complete unit of work.

Use EF's execution strategy for the complete seed transaction with a fresh
context on each attempt. Check the marker again on replay to handle a successful
commit whose acknowledgement was lost. Schema migration execution uses the
provider's supported migration/retry behavior and EF migration locking; do not
wrap migration DDL and seed into an unsupported outer transaction.

Local AppHost has a named development volume. PostgreSQL 18's official image
uses `/var/lib/postgresql` as its volume mount, with PGDATA beneath
`/var/lib/postgresql/18/docker`. Verify Aspire's generated mount and configure
the correct mount explicitly if its default targets an earlier image layout.
Keep data across container recreation, not merely a process restart. Debug and
Release may share this local development database; test mode never shares it.
Document the exact local resource/volume and an explicit reset procedure limited
to that resource, without general Docker pruning.

The reported first-start problem in Synestra has no established diagnosis.
This worker follows the same dedicated-worker pattern, but P1 must verify its
own clean-database first startup, repeat startup, and failure gating. Do not
claim to have fixed or reproduced a defect in another repository.

## One-time demo initialization

Worker demo initialization is explicit configuration, enabled by local AppHost
and disabled by default for other worker runs. Normal integration tests apply
schema migrations only. Seed tests opt in deliberately.

Serialize seed initialization using a transaction-scoped database advisory lock
for the fixed seed identity, then recheck state under the lock:

1. Marker `assignment-demo-v1` exists: skip without modifying business data,
   even if seeded rooms/services were renamed, removed, soft-deleted, or emptied.
2. Marker absent and all business tables empty: insert all demo rooms, their
   current services, the four tariffs, and the marker atomically.
3. Marker absent and any business data present: skip with a clear diagnostic;
   do not overwrite, merge, repair, or create a completion marker.
4. Failure: roll back both demo rows and marker. A later run can try again.

Migration history and other infrastructure metadata do not make a database
non-empty for this decision. Two simultaneous successful seed attempts must
result in one complete seed, without duplicates. Dropping/recreating the
database is the explicit way to reset the marker and business data together.

| Room | Capacity | UAH/hour | Current services, UAH |
| --- | --- | --- | --- |
| Room A | 50 | 2,000 | Projector 500; Wi-Fi 300 |
| Room B | 100 | 3,500 | Projector 500; Wi-Fi 300; Sound 700 |
| Room C | 30 | 1,500 | Wi-Fi 300 |

Every service offering has its own room-specific identity. Service distribution
is the chosen implementation detail; there is no shared catalog.

| Code | UTC daily interval | Multiplier | Unique priority |
| --- | --- | --- | --- |
| Morning | 06:00–09:00 | 0.90 | 0 |
| Standard | 09:00–18:00 | 1.00 | 1 |
| Evening | 18:00–23:00 | 0.80 | 2 |
| Peak | 12:00–14:00 | 1.15 | 3 |

Use readable display names matching these tariffs. For Room A, 11:00–15:00 UTC
with Projector and Wi-Fi produces recorded rental 8,600.000 and total 9,400.000
UAH. This is a useful seed/pricing round-trip acceptance scenario.

## Isolated tests and acceptance

Use a disposable Testcontainers PostgreSQL container per integration suite,
random host ports, no reuse, and no development volume. Each test gets a fresh
uniquely named database built with the real migrations and no demo seed. Drop
only that test's database on disposal, closing its connection pools; dispose
the container after the suite. Keep Resource Reaper enabled for interrupted
runs. Independent concurrent transactions within one scenario use the same
test database, with separate contexts and physical connections.

Real AppHost tests use an explicit isolated mode with unique resources/random
ports and no local development volume, including mounts inherited from default
configuration. They clean up their own resources only. Tests and local AppHost
must be able to run concurrently without changing local rows or its volume.
Tests of development-volume persistence may use their own uniquely named volume,
owned and removed by the test; they must never attach the real local volume.

The following acceptance checks are mandatory. Organize them into meaningful
tests; do not inflate test count with checks of private implementation details.

| Area | Required evidence |
| --- | --- |
| Domain contract | Inclusive money/multiplier bounds; rejection of significant extra precision; trailing-zero acceptance; invalid input leaves state unchanged; globally duplicate priorities rejected even when disjoint; UTC/microsecond and duration edges; midnight, complete coverage and rounding regressions retained. |
| Fresh schema | Real migrations apply to an empty database; repeated migration is a no-op; expected constraints/extension exist; EF detects no pending model changes. |
| Aggregate round trip | Fresh context reloads Room with services and Booking with every snapshot/segment, preserving IDs, ownership, UTC instants, order when explicitly sorted, totals, decimal values, and read-only collections. |
| Snapshot independence | Repricing/removing current services or rules and changing room rate do not change a persisted booking; soft-deleted room and history remain readable. |
| Database values | Raw SQL rejects invalid monetary ranges/precision, NaN/infinities, invalid durations/daily endpoints, out-of-range UTC dates, empty IDs/names and invalid FKs; accepts valid boundary values. |
| Name integrity | Ukrainian/case/whitespace conformance; duplicate active rooms rejected; soft-deleted names reusable; services scoped per room; raw SQL cannot bypass generated keys; simultaneous duplicate writes have only one winner. |
| Priority integrity | Raw SQL and concurrent writes reject duplicate priority regardless of interval; distinct-priority overlaps and cross-midnight rules accepted. |
| Booking overlap | Raw SQL and independent concurrent transactions reject overlap in the same room; adjacency and different rooms accepted; failure rolls back the aggregate without orphan children. |
| Seed | Empty/marked/non-empty-unmarked cases; concurrent runs; repeated startup after edits/deletion; failure rollback; retry after rollback and uncertain commit marker recheck; explicit seed-disabled run leaves business tables empty. |
| Orchestration | Fresh AppHost startup completes migration/seed before healthy API with no restart; second startup preserves data; container recreation retains test-owned development data; migration failure blocks API and gives non-zero worker result; bounded transient retry and cancellation/failure behavior. |
| Isolation | Parallel scenarios get different clean databases; test disposal and suite disposal clean owned resources; local AppHost plus tests do not share databases, fixed ports, or volumes. |
| CI and docs | Linux Docker-backed implemented suites, Release build, archived results/diagnostics, accurate reproducible setup/start/test/reset/migration commands. |

Use deterministic clocks and coordination barriers for concurrency tests rather
than timing sleeps. Keep original numerical test intent while replacing rates
below the approved minimum with valid rates and suitable fractional durations.
The 100-nanosecond adjacency case becomes a 1-microsecond case.

## CI and delivery documentation

Add a GitHub Actions workflow for pull requests and pushes to `main`. Restore
tools/packages, build Release, and run every implemented suite including real
PostgreSQL and AppHost checks on a Docker-capable runner. Use the selected
Microsoft.Testing.Platform CLI and compatible test-report extension, archive
reports and safe failure diagnostics even on failure, and fail on real test
failures. Do not suppress exit code 8 for empty Application.Tests or add dummy
tests; invoke implemented projects explicitly and document the remaining empty
project until B1 introduces its behavior. Update CONTRIBUTING's test guidance
and list the exact verified commands.

On delivery update README, affected decision/status documents, dependency
guidance, solution items, and the roadmap. Include a local-development guide
covering Docker, connection settings, worker flags, design-time EF commands,
logs, isolated tests, and the narrowly scoped local database reset. Keep secrets,
test results, generated build output, and local connection values untracked.

## Ordered implementation plan

The user explicitly approved this complete plan for implementation on 2026-09-15.
Execution does not require rediscovering decisions already settled here.

1. Read required repository sources and this specification; inspect the current
   code, working tree, toolchain, and Docker availability. Report only material
   changes from this baseline. Start `feature/postgres-persistence` from updated
   `main` containing this specification, preserving user changes.
2. Update Domain guards, precision/name helpers, unique-priority validation, and
   minimal `IsDeleted` state; adapt and run meaningful Domain regression tests.
3. Add compatible centrally pinned persistence dependencies, context/mappings,
   schema functions/checks/exclusion constraint, initial migration, and
   design-time creation. Verify actual migration and aggregate round trips.
4. Add worker retry/failure lifecycle and transactional one-time initializer;
   compose PostgreSQL, worker and API in AppHost with correct volume/readiness.
5. Build isolated database and AppHost fixtures; run the acceptance matrix,
   particularly independent concurrent writes and first clean startup.
6. Add CI and accurate development documentation; register all files/projects
   and update P1 evidence. Verify build, implemented suites, migrations/model
   consistency, local startup, documentation paths, and clean diff.
7. Review the entire change against scope and deliver verified local changes
   with a concise report of actual validation and remaining limitations. Mark
   P1 Verified only if all criteria passed; otherwise record actual remaining
   work/check limitations. Publication and merging require the user's separate
   request. Leave Done for a confirmed merge and the narrow post-merge roadmap
   update.

Stop affected work only for a new material incompatibility requiring a changed
requirement/architecture or unavailable external permission. Resolve routine
implementation details inside this scope without repeated approval requests.
Do not weaken constraints or omit difficult startup/concurrency checks to
produce a green result. No diagnosis of Synestra is required for P1 delivery.

## Primary technical references

- [PostgreSQL supported versions](https://www.postgresql.org/support/versioning/)
  and [official image tags and PostgreSQL 18 volumes](https://hub.docker.com/_/postgres).
- [Numeric behavior](https://www.postgresql.org/docs/18/datatype-numeric.html),
  [range constraints](https://www.postgresql.org/docs/18/rangetypes.html#RANGETYPES-CONSTRAINT),
  and [built-in Unicode collations](https://www.postgresql.org/docs/18/collation.html).
- [Npgsql date/time mapping and infinity conversion](https://www.npgsql.org/doc/types/datetime.html)
  and [provider retry configuration](https://www.npgsql.org/efcore/api/Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder.html).
- [EF migration application](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
  and [retry strategies and transaction replay](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).
- [Testcontainers container lifecycle](https://dotnet.testcontainers.org/api/create_docker_container/)
  and [Resource Reaper](https://dotnet.testcontainers.org/api/resource_reaper/).
