# Domain model

This document describes the current domain foundation. It does not imply that
booking creation or pricing calculation is complete.

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
calculation behavior and unresolved policies.

## Intentionally deferred

- A shared service catalog or separate `RoomServiceDefinition` / `RoomServiceType`.
- Service variants and categories.
- A dynamic pricing rule engine beyond daily intervals, multipliers, and priorities.

These abstractions are not needed for the current assignment scope.
