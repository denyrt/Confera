# Domain model

This document describes the current domain foundation and agreed implementation
scope. Intended behavior below does not imply that it is already implemented.

## Implemented foundation

| Model | Responsibility |
| --- | --- |
| `Room` | Room identity, name, capacity, hourly rate, and available services. |
| `RoomService` | A service offered by a specific room, with its own identity and price. |
| `Booking` | Booking interval and creation time in UTC, room reference, pricing snapshots, and total price. |
| `BookingPricingRule` | A daily time interval, multiplier, and priority used to determine pricing and bookable hours. |
| `BookedRoomServiceSnapshot` | The selected service name and price recorded for a booking. |
| `BookingPriceSegment` | A portion of a booking with the applicable tariff and price recorded. |

Room details can be updated through validated methods. Replacing room services
validates the complete input before changing the collection, requires unique
names ignoring case, and preserves existing service IDs when names match.

Booking-input validation requires UTC timestamps, an end after the start, and a
start that is not before the supplied current time. Selected service IDs must be
unique and belong to the room; an empty selection is allowed.

## Intended behavior and remaining work

`Room.Book(...)` is intended to select the room's current services, calculate
pricing, and return a complete `Booking`. It currently validates input and then
throws `NotImplementedException`. `BookingPriceCalculator` is also a stub; its
contract and algorithm remain to be implemented.

Booking creation must generate the booking ID before creating its child snapshots
and segments. Segment ownership, interval coverage, and consistency of the total
price are not yet enforced completely.

Snapshots are intended to keep confirmed booking prices independent of later
changes to room rates, services, or pricing rules.

The ownership of booking history is still to be finalized. `Room` currently
exposes a booking collection, but no implemented behavior populates it. Creating
a booking does not inherently require loading the room's entire booking history.

Application use cases will coordinate availability checks and persistence.
Infrastructure must provide protection against concurrent overlapping bookings;
input validation alone cannot provide this guarantee.

See [booking and pricing decisions](booking-and-pricing-rules.md) for the agreed
time, availability, calculation, room-editing, and deletion policies.

The current models do not yet enforce all these decisions. Remaining work
includes the 30-minute to 24-hour duration bounds, three-digit monetary
precision and rounding, acceptance of rounded zero prices, active-room name
uniqueness, room soft-delete, and restrictions on capacity reduction and deletion
when ongoing or future bookings exist. Service renaming is allowed by the agreed
policy; the current collection replacement matches services by name rather than
providing a dedicated rename operation.

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
