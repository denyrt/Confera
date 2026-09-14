# Domain model

This document describes the current domain foundation and agreed implementation
scope. Intended behavior below does not imply that it is already implemented.

## Implemented foundation

This section describes D1's currently implemented behavior. P1's approved
changes below are still planned; updating the decisions does not imply that
the existing code already enforces them.

| Model | Responsibility |
| --- | --- |
| `Room` | Room identity, name, capacity, hourly rate, and available services. |
| `RoomService` | A service offered by a specific room, with its own identity and price. |
| `Booking` | Booking interval and creation time in UTC, room reference, pricing snapshots, and total price. |
| `BookingPricingRule` | A daily time interval, multiplier, and priority used to determine pricing and bookable hours. |
| `BookedRoomServiceSnapshot` | The selected service name and price recorded for a booking. |
| `BookingPriceSegment` | A portion of a booking with the applicable tariff and price recorded. |
| `BookingRentalSegment` | An immutable calculated rental segment without booking ownership or persistence identity. |

Room details can be updated through validated methods. Replacing room services
validates the complete input before changing the collection, requires unique
names ignoring case, and preserves existing service IDs when names match.

Booking-input validation requires UTC timestamps, a duration of 30 minutes to
24 hours inclusive, and a start that is not before the supplied current time.
Selected service IDs must be unique and belong to the room; an empty selection
is allowed. Room rates and service prices must be positive and have at most
three fractional digits; trailing decimal zeros do not count as extra precision.

`BookingPriceCalculator.Calculate(...)` returns immutable rental segments,
selecting the highest-priority rule at each boundary. It requires full tariff
coverage, handles daily rules crossing midnight, and rounds each segment to
three fractional digits using `MidpointRounding.AwayFromZero`.

`BookingPricingRule.ValidateSet(...)` rejects equal-priority rules whose daily
intervals overlap, including across midnight. Disjoint and adjacent rules may
share a priority. This validates the entire supplied configuration, even when
the conflicting rules do not intersect the requested booking.

`Room.Book(...)` selects the current services, calculates rental segments, and
returns a complete `Booking`. Booking generates its ID before creating its own
service snapshots and price segments. It verifies ordered full segment coverage
and a consistent hourly rate, then calculates total price from the recorded
rounded segment prices and service prices. Rounded zero segments and totals are
allowed. Public collections expose read-only wrappers.

Booking is an independent aggregate; Room owns its current services and does
not hold booking history. Creating a booking does not load or append to a room's
history. Availability checks and saving the returned booking belong to the
application and infrastructure layers.

## Intended behavior and remaining work

Snapshots keep confirmed booking prices independent of later changes to room
rates, services, or pricing rules. Domain tests verify this behavior; persistence
and reporting over those snapshots remain to be implemented.

Application use cases will coordinate availability checks and persistence.
Infrastructure must provide protection against concurrent overlapping bookings;
input validation alone cannot provide this guarantee. The agreed persistence
approach is a short transaction locking the room row for booking and room-change
operations, plus a PostgreSQL exclusion constraint on room ID and the half-open
booking interval. Tariff priorities will be globally unique in Domain and
persistence, replacing D1's allowance for disjoint equal-priority rules.

See [booking and pricing decisions](booking-and-pricing-rules.md) for the agreed
time, availability, calculation, room-editing, and deletion policies.

The current models do not yet enforce all these decisions. Remaining work
includes active-room name uniqueness, room soft-delete, and restrictions on
capacity reduction and deletion when ongoing or future bookings exist. Service
renaming is allowed by the agreed policy; the current collection replacement
matches services by name rather than providing a dedicated rename operation.

The [complete P1 specification](p1-persistence-specification.md) includes minimal
`Room.IsDeleted` state, normalized active-room/service name integrity, inclusive
rate/service/multiplier bounds, microsecond time precision, unique priorities,
EF mappings/migrations, MigrationWorker and isolated real-database tests.
Lifecycle operations and booking-dependent room restrictions remain R1.
Room continues to own services without a `Room.Bookings` navigation.

## API and application scope

Expose the assignment's five operations: create, edit, and delete a room; search
availability; and create a booking with its calculated price. Add two read-only
report endpoints as described below. The booking request uses start and end
timestamps rather than the assignment's start and duration; both describe the
same interval.

Separate public endpoints for price quotes, booking retrieval, cancellation,
rescheduling, and pricing-rule management are outside this scope. Application
may expose internal operations needed by the implementation without adding API
endpoints. This is not a requirement to implement those additional features.

Authentication and roles are outside the technical-assignment implementation
scope. Security work includes input validation, safe error responses without
internal details, data integrity, and protection against concurrent overlapping
bookings. The API is a demonstration without access control; it is not a
production authorization design.

## Initial data and room services

Seed the three rooms and prices from the original assignment: Room A (50 seats,
2,000 UAH/hour), Room B (100 seats, 3,500 UAH/hour), and Room C (30 seats,
1,500 UAH/hour). Use its service examples and prices: projector at 500 UAH,
Wi-Fi at 300 UAH, and sound at 700 UAH, plus its four initial tariffs.

A room's service set is supplied when creating the room and can differ between
rooms. Each offering belongs to that room and has its own price and identity;
there is no shared service catalog. The original assignment does not prescribe
which services belong to each seed room. Selecting varied initial service sets
is an implementation detail, not a requirement that every room offer all three.

P1 records the chosen distribution and unique tariff priorities. Demo seed is
explicit worker behavior outside schema migrations, atomically marked once
only in an empty business database. Later launches preserve edited/deleted
data; an unmarked non-empty database is left unchanged. Tests use fresh isolated
databases without demo data unless testing initialization explicitly.

## Reports

Both reports accept a UTC period with `[start, end)` boundaries. Timestamp input
follows the same explicit-offset convention as bookings. Select bookings whose
start falls within the reporting period and include each selected booking in
full, even if it ends outside that period. Booking-duration limits do not limit
the reporting period. The reports can include future bookings and describe
booking value, not payments received or recognized revenue.

| Report | Grouping | Measures |
| --- | --- | --- |
| Room booking summary | Room ID, with its retained room name for display | Booking count, total booked duration, rental value, service value, and total booking value. |
| Service popularity | Recorded service name | Selection count and total recorded service value. |

Use recorded segment and service prices, never current rates, to calculate
report values. Include bookings for soft-deleted rooms. Room names are not
snapshotted; the room report displays the retained room record's name rather
than promising its name at booking time.

Service popularity groups by the name in each booking snapshot across rooms.
A renamed service appears under its old or new name according to the snapshot;
there is no stable cross-room catalog identity for merging renamed services.

Occupancy percentages are deferred: a reliable denominator would require a
policy for available hours over time, including changes to tariff coverage.

## Intentionally deferred

- A shared service catalog or separate `RoomServiceDefinition` / `RoomServiceType`.
- Service variants and categories.
- A dynamic pricing rule engine beyond daily intervals, multipliers, and priorities.
- Local business time zones and daylight-saving tariff handling.
- Scheduled tariff versions and automatic repricing of confirmed bookings.
- Payments, customer accounts, and additional public booking-management operations.

These abstractions are not needed for the current assignment scope.
