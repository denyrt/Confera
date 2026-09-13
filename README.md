# Confera

Confera is a conference room booking and rental management API built as a technical assignment.

The project covers conference room management, availability search, bookings, rental pricing, additional services, and business-oriented reporting.

## Project status

The solution structure, core domain models, and basic room and booking-input
validation are in place. Booking creation and pricing calculation are still
unfinished. Application use cases, business API endpoints, persistence, initial
data, reports, and behavioral tests remain to be implemented.

The original requirements and project decisions are documented separately:

- [Technical assignment](docs/task-specification.md) ([Ukrainian](docs/task-specification.uk.md)).
- [Domain model, API scope, initial data, and reports](docs/domain-model.md).
- [Booking, pricing, room changes, and deletion rules](docs/booking-and-pricing-rules.md).

The agreed scope is the assignment's five core API operations plus two read-only
reports. Booking and tariff times use UTC; bookings last 30 minutes to 24 hours
with full tariff coverage. Confirmed prices and selected services are preserved
as snapshots. The decision documents describe the target behavior, including
rules that the current domain foundation does not yet enforce.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, branch and commit
conventions, validation commands, and project dependency rules.
