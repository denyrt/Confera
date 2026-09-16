# Domain model

This document describes the current domain foundation and agreed implementation
scope. Intended behavior below does not imply that it is already implemented.

## Implemented foundation

This section describes D1, P1 persistence, B1 booking, R1 room management, and A1 search.

| Model | Responsibility |
| --- | --- |
| `Room` | Room identity, name, capacity, hourly rate, current services, soft deletion, and UUID version for conditional writes. |
| `RoomService` | A service offered by a specific room, with its own identity and price. |
| `Booking` | Booking interval and creation time in UTC, room reference, pricing snapshots, and total price. |
| `BookingPricingRule` | A daily time interval, multiplier, and priority used to determine pricing and bookable hours. |
| `BookedRoomServiceSnapshot` | The selected service name and price recorded for a booking. |
| `BookingPriceSegment` | A portion of a booking with the applicable tariff and price recorded. |
| `BookingRentalSegment` | An immutable calculated rental segment without booking ownership or persistence identity. |
| `RentalPeriod` | An immutable validated UTC interval with microsecond precision and a duration of 30 minutes to 24 hours. |

Room details can be updated through validated methods. Replacing room services
validates the complete input before changing the collection, requires unique
normalized names, and preserves existing service IDs when names match. Matching
services accept updated display spelling and prices; a different normalized
name replaces the offering with a new ID. RoomDetails captures and validates
an entire update before mutation. Real field/service changes rotate Version;
equivalent replacements retain it. Deleted rooms cannot be updated or booked.

Booking-input validation requires UTC timestamps, a duration of 30 minutes to
24 hours inclusive, and a start that is not before the supplied current time.
Selected service IDs must be unique and belong to the room; an empty selection
is allowed. Room rates are 1,000–100,000 UAH and service prices 200–20,000 UAH,
inclusive, with at most three fractional digits. Multipliers are 0.50–2.00 with
at most two digits; trailing zeros do not count as extra precision. External
times must be whole microseconds; `Room.Book` floors its UTC clock input once
before checking the start and recording creation time.

`BookingPriceCalculator.Calculate(...)` returns immutable rental segments,
selecting the highest-priority rule at each boundary. It requires full tariff
coverage, handles daily rules crossing midnight, and rounds each segment to
three fractional digits using `MidpointRounding.AwayFromZero`.

`RentalPeriod.Create(...)` validates the interval once for callers of Room.Book
and the calculator. BookingValidation.RequireBookingPeriod additionally checks
the start against a supplied current time. HasFullCoverage and Calculate share
tariff expansion and segment selection; a coverage query needs no hourly rate.
Missing coverage returns false, while malformed configurations remain errors.

`BookingPricingRule.ValidateSet(...)` rejects every duplicate priority in the
complete configuration, including disjoint, adjacent or masked rules and rules
outside the booking. The database unique constraint protects concurrent writes.

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
rates, services, or pricing rules. Domain and real PostgreSQL round-trip tests
verify this behavior. Reporting over snapshots remains Q1.

CreateBookingService coordinates booking validation, availability, and persistence.
Infrastructure enforces a PostgreSQL exclusion constraint on room ID and the
half-open booking interval, including writes on independent connections. B1
uses a fresh context and Read Committed transaction, locks the room row, then
reads the current room/services and the full tariff set. The clock is read after
lock acquisition and used consistently for validation and creation time. R1
uses the same lock before room/service changes and lifecycle checks.

POST /bookings exposes this use case with explicit-offset timestamp validation,
stable ProblemDetails error codes, price breakdowns, and development Swagger UI.
BookingValidation is shared Domain period/selection validation; expected
BookingValidationException errors are distinct from configuration/invariant
failures. There is no automatic write replay or persisted idempotency key.

See [booking and pricing decisions](booking-and-pricing-rules.md) for the agreed
time, availability, calculation, room-editing, and deletion policies.

RoomManagementService implements creation, read, full replacement and deletion.
It uses an existence query under the shared lock to reject capacity reduction
or deletion if any booking ends after one canonical current UTC instant.
Version preconditions prevent stale changes; the EF mapping also uses Version
as a concurrency token. GET reads current room/services/version in one statement.
History checks belong to Application/Infrastructure, not a Room.Bookings collection.

The [P1 implementation](p1-persistence-specification.md) includes minimal
`Room.IsDeleted` state, normalized active-room/service name integrity, inclusive
rate/service/multiplier bounds, microsecond time precision, unique priorities,
EF mappings/migrations, MigrationWorker and isolated real-database tests.
R1 adds lifecycle operations, booking-dependent restrictions, and a version
migration that preserves existing rows. Room continues to own services without
a `Room.Bookings` navigation.

EF rehydrates aggregates through private constructors and backing collections.
Service removal does not cascade to historical snapshots; physical deletion of
a booked room is restricted. SQL checks enforce per-row ranges and precision,
names, IDs, durations, and foreign keys. Complete segment coverage and total
equality remain Domain invariants, not independently proven by SQL row checks.

Name identity trims the explicit whitespace set and uses PostgreSQL 18 simple
uppercase, including the runtime compatibility mappings documented in
[local development](local-development.md#name-normalization). Keys are generated
by the database and compared with the `C` collation.

## API and application scope

Expose the assignment's five operations: create, edit, and delete a room; search
availability; and create a booking with its calculated price. Add two read-only
report endpoints as described below. R1 also includes GET /rooms/{id}, explicitly
approved to obtain room state and ETag for conditional editing. The booking request uses start and end
timestamps rather than the assignment's start and duration; both describe the
same interval.

The booking operation is implemented under B1. Its
[approved plan](b1-booking-implementation-plan.md#5-http-contract) records the
request, response, and error codes. A1 exposes GET /rooms/availability through
SearchAvailabilityService with start/end, minimum capacity, and page/pageSize.
It returns current room data and service IDs/names/prices for a subsequent booking.
The result includes hasNextPage, without a total count or period-price estimate.
It reads tariffs once, then eligible rooms and services in one paginated query;
missing coverage returns an empty page. See the [A1 contract](a1-availability-search-plan.md).

R1 exposes room creation, full replacement, soft deletion and room-by-ID reading.
PUT and deletion of an active room require If-Match; stale versions return 412
and missing conditions 428. Repeated deletion returns 204 even with an old or
missing version. See the [R1 contract](r1-room-management-plan.md) for complete
request, precondition, response and failure semantics.

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
