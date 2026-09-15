# Confera

Confera is a conference room booking and rental management API built as a technical assignment.

The project covers conference room management, availability search, bookings, rental pricing, additional services, and business-oriented reporting.

## Project status

Domain booking creation and PostgreSQL persistence are implemented. Bookings
preserve UTC microsecond instants, rounded tariff segments, selected services,
and prices as historical snapshots. PostgreSQL enforces active room/service
name integrity, globally unique tariff priorities, and concurrent booking
overlap protection with a half-open range exclusion constraint.

AppHost starts PostgreSQL 18.6, waits for MigrationWorker to apply versioned EF
migrations and optional atomic demo initialization, then starts the API. Local
data survives container recreation in the `confera-postgres-dev` volume.
The worker has bounded retries, a 120-second operation deadline, and a non-zero
result for incomplete or failed initialization.

Application use cases, the five business API operations, reports, and room
lifecycle restrictions remain planned. `Room.IsDeleted` and the filtered unique
name index exist; the deletion operation and booking-dependent guards remain R1.

The original requirements and project decisions are documented separately:

- [Technical assignment](docs/task-specification.md) ([Ukrainian](docs/task-specification.uk.md)).
- [Domain model, API scope, initial data, and reports](docs/domain-model.md).
- [Booking, pricing, room changes, and deletion rules](docs/booking-and-pricing-rules.md).
- [Implementation roadmap, task status, and completion criteria](docs/implementation-roadmap.md).
- [Complete P1 persistence specification and implementation plan](docs/p1-persistence-specification.md).
- [Local development, migrations, tests, and database reset](docs/local-development.md).
- [P1 acceptance evidence](docs/p1-validation.md).

The agreed scope is the assignment's five core API operations plus two read-only
reports. Booking and tariff times use UTC; bookings last 30 minutes to 24 hours
with full tariff coverage. Confirmed prices and selected services are preserved
as snapshots. The decision documents describe the target behavior, including
rules that the current domain foundation does not yet enforce.

## Run and validate

With the SDK selected by `global.json` and Docker running Linux containers:

```powershell
dotnet tool restore
dotnet restore Confera.slnx
dotnet dev-certs https --trust
dotnet build Confera.slnx --configuration Release --no-restore
dotnet run --project orchestration/Confera.AppHost --configuration Release --no-build
```

Use the dashboard resource links for `/health`, `/alive`, and development
OpenAPI. The API has no business endpoints yet. Local initialization creates
Room A/B/C, six room-specific service offerings, and four tariffs once. Later
starts preserve edits and deletions.

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build
```

These are Microsoft.Testing.Platform commands. Database tests use disposable
containers and a fresh database per scenario, without local development volumes.
CI runs the implemented suites on Linux and archives TRX and safe diagnostics.
`Application.Tests` remains empty until B1: the solution-wide test command reports
`Zero tests ran`/exit 8 for that project. This result is not suppressed.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, branch and commit
conventions, validation commands, and project dependency rules.
