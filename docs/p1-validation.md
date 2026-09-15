# P1 validation record

Implementation branch: `feature/postgres-persistence`, based on fetched
`origin/main` commit `b0ec043`, which contains the approved specification
(documentation PR #6). The initial working tree was clean. Implementation is
local and unmerged; no PR or merge is part of this delivery.

## Environment and command results

Validation date: 2026-09-15. Both environments used SDK 10.0.401, runtime
10.0.12, EF tool 10.0.12, Aspire 13.5.3 and `postgres:18.6`.
Windows used Docker Desktop's Linux engine 29.7.2 and `desktop-linux` context,
with `DOCKER_HOST` unset. Linux used Ubuntu 24.04, Docker Engine 29.1.3,
and its own daemon/socket, with Resource Reaper enabled.

The following commands were executed from the repository root. Test commands
use Microsoft.Testing.Platform, with no VSTest flags or ignored test failures.

```powershell
dotnet tool restore
dotnet restore Confera.slnx
dotnet dev-certs https --trust
dotnet build Confera.slnx --configuration Release --no-restore
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/domain
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/apphost
```

| Check | Windows | Linux |
| --- | --- | --- |
| Tool/package restore | Passed | Passed |
| Release build | 0 warnings, 0 errors | 0 warnings, 0 errors |
| Domain | 88 passed, 0 skipped | 88 passed, 0 skipped |
| PostgreSQL integration | 14 passed, 0 skipped | 14 passed, 0 skipped |
| AppHost and worker | 12 passed, 0 skipped | 12 passed, 0 skipped |
| Pending EF model changes | None | None |

Linux sets `SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"`
before certificate trust and all test runs, as in CI. Windows already had a
trusted development certificate.

The Windows AppHost run completed in 3m36s while local AppHost was running.
The final Linux AppHost run completed in 4m46s. Each OS passed all 114 tests
across the implemented suites. The final Linux TRX run timestamps are
`11_20_00.5278817` (Domain), `11_20_20.3064503` (Integration), and
`11_25_07.0983399` (AppHost); all are dated 2026-09-15.
The worker deadline test exercises the real 120-second deadline. TRX files
remain in ignored `artifacts/tests`; the Linux rehearsal's results are in
`artifacts/linux-work/artifacts/tests`. Safe resource state/health/exit-code
JSON diagnostics are in the corresponding `artifacts/diagnostics` directories.

The separate command below reported **zero tests and MTP exit code 8** (the
outer `dotnet test` process returned 1). This is expected unfinished B1 test
coverage, not a passing suite. It is neither suppressed nor replaced by a stub.
The solution retains the project; CI explicitly invokes implemented suites.

```powershell
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build
```

The real initial migration was generated with `dotnet ef migrations add
InitialPersistence --project src/Confera.Infrastructure --output-dir
Persistence/Migrations`, then extended with the specified schema functions and
exclusion SQL. These subsequent commands passed; connection configuration was
supplied only through the process environment:

```powershell
dotnet ef migrations list --project src/Confera.Infrastructure --configuration Release --no-build
dotnet ef database update --project src/Confera.Infrastructure --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/Confera.Infrastructure --configuration Release --no-build
dotnet ef migrations script --idempotent --project src/Confera.Infrastructure --configuration Release --no-build --output artifacts/initial-persistence.sql
```

The migration list contained `20260914235107_InitialPersistence`; update on the
running local database was a no-op; model comparison found no changes; SQL
script generation succeeded. Fresh databases in tests use `MigrateAsync`.

## Acceptance matrix

This maps every row of [the approved matrix](p1-persistence-specification.md#isolated-tests-and-acceptance)
to executable checks and the manual local-isolation evidence below.

| Area | Evidence |
| --- | --- |
| Domain contract | `PersistenceContractTests`, `RoomMoneyTests`, `BookingPricingRuleTests`, `BookingTests`, and calculator regressions cover inclusive bounds, significant extra precision, trailing zeros, atomic replacement, globally duplicate priorities, UTC kind, rejected sub-microsecond input, floored current time, duration/midnight/coverage, and per-segment rounding including zero. |
| Fresh schema | `RealMigrationsAreRepeatableAndMatchModel` applies/reapplies the real migration, checks migration history, `btree_gist`, exclusion constraint and model consistency. Every database scenario starts separately. EF CLI also confirms model consistency. |
| Aggregate round trip | `AggregatesRoundTripAndHistorySurvivesConfigurationChanges` reloads with fresh contexts and compares IDs, parent IDs, sorted segments, snapshot fields, totals and read-only collections. `FiniteDateTimeLimitsRoundTripWithoutInfinityConversion` proves both finite UTC limits and microsecond preservation. |
| Snapshot independence | The aggregate test changes rate and services, removes current services/rules, soft-deletes Room, and reloads unchanged historical values. Physical deletion remains restricted. No snapshot FK points at current services or tariffs. |
| Database values | `SqlValueChecksRejectInvalidInputsAndAcceptInclusiveBounds` writes SQL directly against every monetary column, checks lower/upper/extra-precision/NaN/infinity/NULL cases, IDs, FK ownership, names/UTF-16 limits, capacity, finite dates, duration and daily endpoints, with accepted boundary cases. |
| Name integrity | `NameKeysConformForEveryUnicodeScalarAndPreventBypass` compares every PostgreSQL-representable Unicode scalar in batches and named multilingual/whitespace regressions; checks generated-key bypass, active uniqueness, deleted-name reuse and room-scoped services. `IndependentConcurrentDuplicateNamesHaveOneWinner` uses two physical connections. |
| Priority integrity | `GlobalPriorityProtectsDisjointConcurrentWritesAndAllowsDistinctOverlaps` proves one concurrent winner for a duplicate global priority, direct-SQL rejection, and valid distinct-priority/midnight overlaps. Domain rejects duplicates across the complete rule set. |
| Booking overlap | `BookingExclusionProtectsIndependentConnectionsAndRollsBackChildren` tests direct SQL, two physical connections coordinated by a barrier, one overlapping winner, adjacent/different-room concurrent success, and rollback without orphan snapshots/segments. |
| Seed | All five `SeedTests` cover four simultaneous initializers with a barrier, one completion marker, exact demo rows and Room A total 9400, edits/deletion/emptied marked database, non-empty unmarked skip, rollback, fresh-context transient replay, and lost-commit-acknowledgement marker recheck. `SeedDisabledWorkerAppliesSchemaOnlyAndStopsSuccessfully` covers explicit opt-out. |
| Orchestration | `FreshStartupMigratesAndSeedsBeforeHealthyApiWithoutRestart` records worker exit 0 before API Running/healthy. Failure-gating test executes an actual invalid-schema worker and observes nonzero exit/API FailedToStart. The recreation test verifies two different containers, actual mount, server version/PGDATA and retained edited data. Seven worker cases cover recovery, six-retry bound/delay, non-retried permanent SQLSTATEs, shutdown and actual deadline cancellation. |
| Isolation | `ParallelDatabasesAreCleanAndDisposalDropsOnlyOwnedDatabase`, concurrent AppHosts, fresh random databases/ports, empty mount annotations, and test-owned volume cleanup are checked. The manual local run below confirms unchanged local rows/volume while suites execute. Successful suite disposal left no test containers or test-owned named volumes. |
| CI and docs | The Linux workflow restores/builds, creates the development certificate, runs all implemented suites, and always archives TRX/safe diagnostics. Local Windows/Linux commands, EF commands, two local startup profiles, document/solution paths and diff whitespace are verified. Hosted GitHub Actions execution awaits publication; no remote run is claimed. |

SQL CHECK/FK constraints enforce the documented per-row rules. Complete segment
coverage, total equality and selected-service ownership remain Domain invariants
on the supported aggregate write path; P1 does not claim SQL checks prove them
for arbitrary cross-row writes.

## Local startup and coexistence

These two real local runs used the same named development volume:

```powershell
dotnet run --project orchestration/Confera.AppHost --configuration Release --no-build --launch-profile http
dotnet run --project orchestration/Confera.AppHost --configuration Release --no-build
```

The first clean run created 3 rooms, 6 services, 4 tariffs and one marker without
restarting AppHost; API `/health` returned HTTP 200. All Windows AppHost/worker
tests ran alongside it, including two simultaneous isolated AppHosts. Integration
tests also ran alongside local AppHost. MD5 checksums of sorted complete rows
in Rooms, RoomServices, BookingPricingRules and InitializationMarkers were
identical before and after testing and after the second local launch.

PostgreSQL changed from container `195de53ea36e` to `073795d81067`; both mounted
only `confera-postgres-dev` at `/var/lib/postgresql`. `SHOW data_directory`
returned `/var/lib/postgresql/18/docker`; `SHOW server_version` returned
`18.6 (Debian 18.6-1.pgdg13+2)`. The second API health check returned 200.
Both server and application database encoding were confirmed as UTF8.
No test mounted the local volume. Local digest SQL/results and logs are ignored
under `artifacts/`; connection values and dashboard tokens are not delivery files.
The scoped reset procedure is documented in the local-development guide; the
real local database was retained. The equivalent drop/recreate behavior is
exercised with disposable test databases and the test-owned volume.

## Material findings resolved during validation

- PostgreSQL 18 and .NET case tables differed for 28 scalars on Windows. The
  documented compatibility mappings now pass full scalar conformance on both OSes.
- Npgsql can initialize before context configuration in a test/admin connection.
  Runtime host options disable infinity conversion before any provider use;
  finite minimum/maximum round trips now pass.
- Npgsql's migration-history preflight needs the worker's outer execution
  strategy; retry attempts use fresh contexts while EF owns migration locking
  and transactions. The worker exit result is captured before `RunAsync`
  disposes its service provider.
- Aspire 13.5.3's CLI run hook stopped a test instance of the same project and
  selected a different build configuration. Direct .NET launch plus independent
  test backchannel identities fixes coexistence; the CLI bundle remains enabled.
- Clean Linux SDK images do not necessarily generate/trust an HTTPS certificate.
  Validation first found `Unable to configure HTTPS endpoint`, then HTTPS health
  checks rejected `UntrustedRoot`. Explicit `dotnet dev-certs https --trust` and
  the OpenSSL `SSL_CERT_DIR` configuration are now in setup/CI. TLS validation
  remains enabled.
- A Windows named-pipe override worked for Testcontainers but failed Docker CLI;
  validation uses normal `desktop-linux` discovery without `DOCKER_HOST`.
  Restricted sandbox access to NuGet configuration required rerunning the build
  with normal host permissions; that build passed without warnings.

The Linux rehearsal initially failed before tests because Docker Desktop host
networking could not route Resource Reaper, then because nested overlay storage
was unsupported. The final rehearsal used a separate privileged validation
container with an internal Docker daemon, VFS storage and no host Docker socket
or development volume. This is a local test harness, not an application/CI
dependency. The same restore/build/test commands run directly on CI's Linux host.
Its ignored Dockerfile, daemon/validation scripts and source copy are retained
under `artifacts/` as the local command record. Resource Reaper stayed enabled.

The local harness was built and started with the following commands after
copying repository source (excluding ignored build output) to `artifacts/linux-work`:

```powershell
docker build -f artifacts/linux-validation.Dockerfile -t confera-p1-validation:local artifacts
$linuxRoot = Join-Path (Get-Location) 'artifacts/linux-work'
docker run --name confera-p1-linux-validation --privileged --mount "type=bind,source=$linuxRoot,target=/work" --workdir /work confera-p1-validation:local bash /work/daemon.sh
```

After correcting harness setup/certificate trust, the final run used
`docker start -a confera-p1-linux-validation`. `daemon.sh` starts only its internal
daemon with `--storage-driver=vfs --feature=containerd-snapshotter=false`, then
executes the validation script. This harness is optional; normal Linux hosts
run the documented commands directly without a nested Docker daemon.

## Delivery boundary

Application behavior B1/R1/A1/H1/Q1 remains out of scope. P1 adds no business
endpoint, generic repository, shared service catalog, report, Room-to-booking
history navigation, or mutable snapshot API. Domain has no infrastructure
dependency. Final documentation validation checked 46 local links/anchors,
33 solution paths and all 12 Markdown registrations. `git diff --check` and
whitespace checks including untracked delivery files passed; changed C# files
were formatted with `dotnet format whitespace Confera.slnx --no-restore --include`
and the changed/new C# path list. The local validator is retained at
`artifacts/validate-docs.ps1`.

P1 is **Verified** against the entire acceptance matrix. Implementation remains
local and unmerged, with no PR created. Done requires a separately confirmed
merge. Hosted GitHub Actions has not been run because these changes have not
been published; its build/test sequence passed in the local Linux rehearsal.
The completed Linux validation container and its helper image were removed;
the development volume and unrelated pre-existing volumes were preserved.

## NameIdentity follow-up

After the initial P1 matrix validation, the span-based name helper was refined
on 2026-09-15: both public methods explicitly reject null, and the uppercase
loop encodes each Rune into a two-character stack buffer reused outside the
loop, appending only the written span. This avoids `Append(object)` boxing and
per-Rune string conversion. Trimming and case mappings retain the P1 contract.

The following checks passed on Windows after this refinement: 90 Domain tests
(including null rejection and alternating supplementary/BMP characters), and
the full Unicode scalar conformance/generated-key integrity test against
PostgreSQL 18.6. The complete Windows/Linux matrix above records the initial
P1 validation; these are the subsequent focused regression results.

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --report-trx --results-directory artifacts/tests/nameidentity-domain
dotnet test --project tests/Confera.Integration.Tests --configuration Release --filter-method '*NameKeysConform*' --report-trx --results-directory artifacts/tests/nameidentity-postgres
```

Changed C# files were formatted and `git diff --check` passed.
