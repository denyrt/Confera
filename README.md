# Confera

Confera is a conference room booking and rental management API built as a technical assignment.

The project covers conference room management, availability search, bookings, rental pricing, additional services, and business-oriented reporting.

## Project status

The solution structure and core domain models are in place. Domain booking
creation calculates fully covered UTC tariff segments and preserves prices and
selected services as snapshots. Behavioral domain tests cover pricing, input
validation, snapshots, and pricing-rule priority overlaps.

Application use cases, business API endpoints, persistence, initial data,
reports, room soft-delete, and booking-dependent room-change restrictions remain
to be implemented. The domain alone does not check room availability or protect
against concurrent bookings.

The original requirements and project decisions are documented separately:

- [Technical assignment](docs/task-specification.md) ([Ukrainian](docs/task-specification.uk.md)).
- [Domain model, API scope, initial data, and reports](docs/domain-model.md).
- [Booking, pricing, room changes, and deletion rules](docs/booking-and-pricing-rules.md).
- [Implementation roadmap, task status, and completion criteria](docs/implementation-roadmap.md).
- [Complete P1 persistence specification and implementation plan](docs/p1-persistence-specification.md).

The agreed scope is the assignment's five core API operations plus two read-only
reports. Booking and tariff times use UTC; bookings last 30 minutes to 24 hours
with full tariff coverage. Confirmed prices and selected services are preserved
as snapshots. The decision documents describe the target behavior, including
rules that the current domain foundation does not yet enforce.

P1 planning is complete; implementation remains Planned. Its specification
includes the approved validation changes, globally unique tariff priorities,
PostgreSQL 18.6, MigrationWorker, atomic one-time demo initialization, isolated
tests, and CI. These facilities are not present in the current implementation.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, branch and commit
conventions, validation commands, and project dependency rules.
