# Local development

## Prerequisites and startup

Use the .NET SDK selected by `global.json` (10.0.401), Docker with Linux
containers, and the repository-local `dotnet-ef` tool. PostgreSQL is pinned to
`postgres:18.6` for AppHost and tests. No separately installed database is needed.

```powershell
docker info
dotnet tool restore
dotnet restore Confera.slnx
dotnet dev-certs https --trust
dotnet build Confera.slnx --configuration Release --no-restore
dotnet run --project orchestration/Confera.AppHost --configuration Release --no-build
```

Open the dashboard URL printed by AppHost and use its API resource endpoint.
The development API exposes `/health`, `/alive`, and `/openapi/v1.json`.
There are no business controllers yet. Stop AppHost with Ctrl+C.

The default launch profile uses HTTPS and the local ASP.NET development
certificate. Create and trust it explicitly on a clean SDK installation,
including Linux CI images that disable first-run certificate generation.
Aspire's API health check uses HTTPS and requires a trusted certificate too.
On Ubuntu, set the OpenSSL trust path before the setup command and subsequent
AppHost/test runs ([Microsoft's Linux certificate guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0#openssl-trust)):

```bash
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"
dotnet dev-certs https --trust
```

For an HTTP AppHost dashboard, append `--launch-profile http`; that profile
explicitly permits unsecured Aspire transport on localhost. The API still has
its own HTTPS endpoint and needs the development certificate.
The project retains the Aspire CLI bundle for DCP/dashboard discovery, but
sets `ASPIRE_SUPPRESS_CLI_RUN_HOOK=true` so `dotnet run` executes the selected
Debug/Release build directly. Aspire 13.5.3's CLI hook otherwise selects an
existing project instance and can run a different build configuration.

On Windows with Docker Desktop, keep the selected context in Linux-container
mode and normally leave `DOCKER_HOST` unset. Docker CLI and Testcontainers 4.12
parse explicit named-pipe URIs differently. If a previous shell set an override:

```powershell
Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue
docker context ls
docker info
```

The validated local environment used the `desktop-linux` context. Do not commit
machine-specific pipe addresses. On Linux, the usual Docker socket discovery
works without an override.

## Resources, connections, and lifecycle

| Resource | Purpose |
| --- | --- |
| `confera-postgres` | PostgreSQL 18.6 server, random host port |
| `confera` | UTF-8 application database and logical connection name |
| `confera-migrations` | One-shot MigrationWorker |
| `confera-api` | API, started only after worker exit 0 |
| `confera-postgres-dev` | Local named Docker volume mounted at `/var/lib/postgresql` |

The PostgreSQL 18 image puts PGDATA at `/var/lib/postgresql/18/docker`.
AppHost explicitly mounts the parent `/var/lib/postgresql`; the older
`/var/lib/postgresql/data` mount is not used. Debug and Release share this local
volume. Stopping AppHost removes its session resources but retains development
data across a new PostgreSQL container.

Aspire supplies `ConnectionStrings__confera` to both API and worker. Obtain a
local connection from the dashboard when needed; do not paste it into tracked
files or logs. Aspire manages the PostgreSQL password parameter; an explicit
value can be supplied through AppHost user secrets if desired:
`Parameters:confera-postgres-password`. Use a stable value for an existing volume.

Startup order is PostgreSQL/database readiness → MigrationWorker completion →
healthy API. API registration includes a database health check but does not
migrate, seed, or install a database retry strategy. A worker failure/cancellation
returns a non-zero exit code and API does not start.

API, worker, and database-test runtime configuration disable Npgsql DateTime
infinity conversions before provider initialization. The design-time factory
also sets the switch before creating its provider options. Finite UTC minimum
and maximum microsecond instants are verified by a real database round trip.

## Worker and demo initialization

Standalone worker runs always apply migrations. `DemoSeed:Enabled` defaults to
false; local AppHost explicitly sets it to true. To run against a connection
already supplied through configuration:

```powershell
dotnet run --project src/Confera.MigrationWorker --configuration Release --no-build -- --DemoSeed:Enabled=false
```

Supply `ConnectionStrings__confera` through your environment or an appropriate
secret provider before running this command. Set `--DemoSeed:Enabled=true` to
request the assignment demo data. Migrations themselves contain no demo rows.

The initializer uses an advisory transaction lock for `assignment-demo-v1`.
It writes Room A/B/C, their six room-specific service offerings, four tariffs,
and a completion marker in one transaction. A marker skips initialization even
after all business data is edited or deleted. An unmarked non-empty database is
left unchanged and no marker is written. Failure rolls back rows and marker;
replay uses a fresh context and rechecks the marker, including uncertain commits.

Worker Npgsql retries allow at most six retries with at most five seconds between
attempts. A linked 120-second deadline covers migration and optional seed;
shutdown cancellation also cancels database operations and retry delays. An EF
execution strategy surrounds the complete migration call because Npgsql 10.0.2
reads history before EF's internal retry scope. Each attempt gets a fresh context;
EF retains ownership of migration transactions and locking. Migration and demo
seed are separate phases, with no external transaction around migration DDL.
Provider authentication, invalid-schema, and business-constraint errors are not
retried. Worker logs identify the phase, exception type, and SQLSTATE without
credentials. Successful initialization stops the worker.

## Migrations without AppHost

Infrastructure owns the context, design-time factory, initial migration, and
model snapshot. Configure `ConnectionStrings__confera` outside tracked files
for commands that connect to a database:

```powershell
dotnet ef migrations list --project src/Confera.Infrastructure --configuration Release
dotnet ef database update --project src/Confera.Infrastructure --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Confera.Infrastructure --configuration Release
dotnet ef migrations script --idempotent --project src/Confera.Infrastructure --configuration Release --output artifacts/initial-persistence.sql
```

For model-only/script generation, `Host=localhost;Database=design_time_only`
is a harmless placeholder: those commands do not connect. To add a future
migration after an approved model change:

```powershell
dotnet ef migrations add DescribeChange --project src/Confera.Infrastructure --output-dir Persistence/Migrations
```

Review custom SQL and generated files together. The initial migration creates
`btree_gist`, immutable name/UTF-16 functions, generated name keys, per-row checks,
foreign keys, indexes, and the booking exclusion constraint. Tests exercise
`MigrateAsync`, never `EnsureCreated`. Do not replace or rewrite an already
deployed migration when later work introduces schema changes.

## Isolated tests and diagnostics

Use Microsoft.Testing.Platform syntax, not VSTest `--filter` or `--logger`:

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/domain
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/apphost
```

For a focused run, use e.g. `--filter-method '*FreshStartup*'`. The worker deadline
test intentionally waits approximately 120 seconds. Give the full AppHost suite
several minutes. `Application.Tests` is empty until B1; a solution-wide test run
therefore reports exit 8 for that project. Do not hide or replace that result.

Each database-test assembly owns one disposable Testcontainers PostgreSQL 18.6
container, uses random ports and no reuse, and leaves Resource Reaper enabled.
Every scenario creates a uniquely named database with real migrations and no
demo data by default. Database disposal closes its pool and drops only that
database; assembly disposal removes its container. Concurrent writes within
one scenario use distinct physical connections to the same database.

Real AppHost tests supply `Persistence:Isolated=true` before resource creation.
They use unique Aspire resource instances and random ports, and do not inherit
the named local development volume. The recreation scenario attaches its own
`confera-p1-<random>` volume at the development mount, inspects the actual Docker
mount/PGDATA, recreates the container, and removes only that test-owned volume.
Tests can run alongside a local AppHost without changing its rows or volume.
Each test host also gets a unique `AppHost:FilePath` configuration identity for
Aspire's auxiliary CLI socket. This prevents two in-process test hosts from
overwriting a socket and prevents a local CLI invocation from stopping tests
as a previous instance of the same project. Project/resource paths stay intact.

TRX reports are under `artifacts/tests`. AppHost resource state transitions,
health and exit codes are archived under `artifacts/diagnostics`; they exclude
environment variables and connection strings. Use the local Aspire dashboard
for detailed service logs. CI runs every implemented suite on a Docker-capable
Linux runner and uploads reports/diagnostics even when a test fails.

## Reset only the local development database

Resetting destroys this Confera development database, including its initialization
marker. Stop its AppHost, then confirm no container still uses the named volume:

```powershell
docker ps -a --filter volume=confera-postgres-dev
```

When the list is empty, explicitly reset that volume:

```powershell
docker volume rm confera-postgres-dev
dotnet run --project orchestration/Confera.AppHost --configuration Release --no-build
```

The next launch recreates, migrates, and initializes a fresh database. Do not
use general Docker pruning or delete volumes belonging to other repositories.
If a container is still listed, stop its owning AppHost before proceeding.

## Name normalization

`NameIdentity` and `confera_name_key` trim the explicitly enumerated Unicode
whitespace set in the P1 specification and use Unicode simple uppercase. Keys
are compared ordinally (`C` collation). Display spelling, internal spaces,
accents, composed/decomposed forms, and distinct alphabets are preserved.
PostgreSQL generated columns prevent a forged key through direct SQL.

The Windows runtime conformance test found these PostgreSQL 18 baseline mappings
missing from invariant .NET uppercase; `NameIdentity` supplies them explicitly:

| Lower scalar(s) | Upper scalar(s) |
| --- | --- |
| U+0131 | U+0049 |
| U+019B | U+A7DC |
| U+0264 | U+A7CB |
| U+1C8A | U+1C89 |
| U+A7CD | U+A7CC |
| U+A7DB | U+A7DA |
| U+10D70–U+10D85 | U+10D50–U+10D65 |

The batched test compares every PostgreSQL-representable Unicode scalar (NUL
and surrogate code points excluded) plus named multilingual regressions. Keep
this conformance test when changing the runtime or PostgreSQL baseline. Database
UTF-16 length checks also count supplementary scalars as two code units.

## Dependency baseline

P1 uses EF Core/Relational/Design 10.0.12, Npgsql EF provider 10.0.2, Aspire
PostgreSQL hosting/testing 13.5.3, Testcontainers.PostgreSql 4.12.0, and MTP TRX
extension 2.1.0 with existing xUnit v3 MTP package 4.0.0. Versions live centrally
in `Directory.Packages.props`. Test projects explicitly pin SSH.NET 2026.0.0 to
replace Testcontainers' vulnerable 2025.1.0 transitive dependency
([upstream advisory](https://github.com/sshnet/SSH.NET/security/advisories/GHSA-q939-rpr3-3284)).
