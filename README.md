# Confera

Confera is a conference room booking and rental management API built as a technical assignment.

The project covers conference room management, availability search, bookings, rental pricing, additional services, and business-oriented reporting.

## Project status

Room creation, reading, replacement and soft deletion, transactional booking
creation through `POST /bookings`, Domain pricing, and PostgreSQL persistence
are implemented. Bookings
preserve UTC microsecond instants, rounded tariff segments, selected services,
and prices as historical snapshots. PostgreSQL enforces active room/service
name integrity, globally unique tariff priorities, and concurrent booking
overlap protection with a half-open range exclusion constraint.

AppHost starts PostgreSQL 18.6, waits for MigrationWorker to apply versioned EF
migrations and optional atomic demo initialization, then starts the API. Local
data survives container recreation in the `confera-postgres-dev` volume.
The worker has bounded retries, a 120-second operation deadline, and a non-zero
result for incomplete or failed initialization.

The booking use case locks the room, reads its current services and tariffs,
checks availability, and commits the complete booking atomically. It rejects
absent/deleted rooms and returns stable HTTP errors. Development Swagger UI
documents the operation and its price breakdown.

Room updates and deletion share booking's room-row lock. Capacity reduction and
deletion reject ongoing/future bookings. GET returns an ETag; PUT and deletion
of an active room require If-Match to prevent stale writes. Room/service changes
rotate a UUID version; no-op replacements and bookings retain it. Availability
search and both reports remain planned. The roadmap records validation and
merge status separately from implemented behavior.

The original requirements and project decisions are documented separately:

- [Technical assignment](docs/task-specification.md) ([Ukrainian](docs/task-specification.uk.md)).
- [Domain model, API scope, initial data, and reports](docs/domain-model.md).
- [Booking, pricing, room changes, and deletion rules](docs/booking-and-pricing-rules.md).
- [Implementation roadmap, task status, and completion criteria](docs/implementation-roadmap.md).
- [Complete P1 persistence specification and implementation plan](docs/p1-persistence-specification.md).
- [Approved B1 booking implementation plan and API contract](docs/b1-booking-implementation-plan.md).
- [Approved R1 room management and concurrency contract](docs/r1-room-management-plan.md).
- [Local development, migrations, tests, and database reset](docs/local-development.md).
- [P1 acceptance evidence](docs/p1-validation.md).

The agreed scope is the assignment's five core API operations plus two read-only
reports and a supporting room-by-ID read for conditional editing. Booking and
tariff times use UTC; bookings last 30 minutes to 24 hours
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

Use the dashboard's API resource link for `/swagger`, `/openapi/v1.json`,
`/health`, and `/alive`. Local initialization creates
Room A/B/C, six room-specific service offerings, and four tariffs once. Later
starts preserve edits and deletions.

`POST /bookings` requires `roomId`, `start`, `end`, and `serviceIds` (`[]` is
allowed). Timestamps require `Z` or an explicit offset, normalize to UTC, and
must have whole-microsecond precision. A successful response is `201` with the
booking ID, UAH total, rental segments, and recorded services. For the original
Room A data, 11:00-15:00 UTC with Projector and Wi-Fi costs 9,400 UAH.
See the [booking example and ID lookup](docs/local-development.md#try-a-booking)
and [HTTP request file](src/Confera.Api/Confera.Api.http).

Room management uses `POST /rooms`, `GET /rooms/{id}`, `PUT /rooms/{id}` and
`DELETE /rooms/{id}`. POST returns 201 and Location; GET returns current data,
service IDs and ETag. Copy ETag into If-Match for PUT/DELETE. Missing conditions
return 428, stale versions 412, and business conflicts 409. PUT requires all
fields and the complete services array; `[]` removes all current services.
It returns 200 with current data; GET supplies the next ETag. Repeated deletion
returns 204, including an old or absent condition. See the
[room editing example](docs/local-development.md#manage-rooms-with-etag).

Overlaps return `409`; adjacent bookings are allowed. Booking writes have no
automatic retries or idempotency keys. A lost response may follow a successful
commit, so repeating the same request can return a conflict. This demonstration
API has no authentication, payments, or public booking retrieval/cancellation.

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build
```

These are Microsoft.Testing.Platform commands. Database tests use disposable
containers and a fresh database per scenario, without local development volumes.
CI runs the implemented suites on Linux and archives TRX and safe diagnostics.
All four test projects now contain implemented tests. HTTP tests use the real
API pipeline and isolated PostgreSQL databases; application clocks are controlled.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, branch and commit
conventions, validation commands, and project dependency rules.
