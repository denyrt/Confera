# Booking and pricing decisions

These are project decisions used to interpret the
[technical assignment](task-specification.md). They define intended behavior,
not completed implementation. See the [domain model](domain-model.md) for the
current implementation status and API scope.

## Time and tariff coverage

- Pricing rules define both bookable hours and the applicable rental price.
  There is no separate opening-hours model and no fallback tariff.
- Every portion of a booking must be covered by at least one pricing rule.
  Any uncovered interval rejects the entire booking; partial pricing is not a
  valid result.
- The assignment's initial rules cover 06:00–23:00. This is initial tariff
  configuration, not a hard-coded domain restriction.
- Intervals use `[start, end)` boundaries: the start is included and the end is
  excluded. Adjacent intervals therefore do not overlap at their shared boundary.
- Rules may cross midnight. When a rule's end time is earlier than its start
  time, the end belongs to the following day. Equal start and end times are
  currently rejected rather than interpreted as a full-day rule.
- Booking duration must be at least 30 minutes and at most 24 hours, inclusive.
  It need not be a multiple of 30 minutes. The maximum is an independent limit,
  not permission to book uncovered hours. There is no additional advance-booking
  horizon limit in the current scope.
- Bookings may cross midnight. Coverage is checked across the entire interval,
  including every affected date.
- Both booking timestamps and daily tariff hours use UTC. For example, the
  standard tariff starts at 09:00 UTC, not 09:00 in a room's local time zone.
  Daylight-saving transitions therefore do not affect tariff calculations.
  Local business time zones are outside the current scope.
- API requests use `start` and `end` timestamps in ISO 8601 with `Z` or an
  explicit UTC offset. Accept them as `DateTimeOffset` and normalize to UTC
  before domain processing. Reject timestamps without an offset rather than
  interpreting them in the server's local time zone.
- A booking cannot start in the past, and its end must be after its start.

For example, a 04:00–14:00 booking is rejected by the initial configuration because
04:00–06:00 has no tariff coverage. A later night rule can make that interval
bookable without changing the coverage requirement.

## Availability

The entire room is rented, not individual seats. Search returns only rooms that
are not deleted, have capacity greater than or equal to the requested capacity,
have no overlapping booking, and have valid tariff coverage for the full
requested interval. The same period limits apply to search and booking.

Availability must be checked again when creating a booking. Search does not
reserve a room. Application use cases coordinate these checks, and persistence
must prevent overlapping bookings even when requests arrive concurrently.
Adjacent bookings are allowed under the `[start, end)` interval convention.

## Segments and rule selection

Split the booking at every applicable rule boundary. For each resulting segment,
select the covering rule with the highest `Priority`. Peak pricing takes
precedence over standard pricing; multipliers are not combined.

If multiple covering rules share the highest priority, reject the calculation as
ambiguous. Segment duration must be positive. The resulting segments must cover
the booking completely, stay within its boundaries, and have no gaps or overlaps.

The initial tariffs are standard 09:00–18:00 at 1.00, morning 06:00–09:00 at
0.90, evening 18:00–23:00 at 0.80, and peak 12:00–14:00 at 1.15. Peak has a
higher priority than standard.

For an 11:00–15:00 UTC booking, the intended segments are:

| Segment | Tariff | Multiplier |
| --- | --- | --- |
| 11:00–12:00 | Standard | 1.00 |
| 12:00–14:00 | Peak | 1.15 |
| 14:00–15:00 | Standard | 1.00 |

## Price and service snapshots

- Segment price is elapsed duration in hours multiplied by the room's hourly
  rate and the selected rule's multiplier. Fractional hours are charged
  proportionally rather than rounded up to whole hours.
- Elapsed duration is calculated between UTC instants.
- Each selected service is charged once per booking using the room's current
  price. Tariff multipliers apply only to room rental, never to services.
  Unknown or duplicate service IDs are rejected; an empty selection is allowed.
- Money is denominated in UAH and calculated using `decimal`. Room rates and
  service prices must be positive and have at most three fractional digits;
  reject more precise inputs rather than silently rounding them.
- Round each segment price to three fractional digits using
  `MidpointRounding.AwayFromZero`. The total is the sum of the recorded, rounded
  segment prices and service prices, so the breakdown agrees with the total.
- A segment price that rounds to zero is allowed. The 30-minute minimum applies
  to the whole booking, not each tariff segment. A zero total after rounding is
  also allowed: positive rates and multipliers do not guarantee a positive
  rounded result for arbitrarily small values.
- Record the room hourly rate, selected service names and prices, and segment
  tariff codes and multipliers as snapshots. Later configuration changes must
  not recalculate confirmed booking prices.

For Room A at 2,000 UAH per hour, 11:00–15:00 UTC costs
`2000 * (1 + 2 * 1.15 + 1) = 8600.000` UAH. If projector and Wi-Fi are offered
and selected at the assignment's prices, the total is `9400.000` UAH.

## Changes after confirmation

Confirmed bookings and their pricing snapshots are not recalculated or mutated
when room rates, service offerings, or pricing rules change. Changes apply to
new bookings using the configuration effective at creation; scheduled tariff
versions and a future-booking recalculation cutoff are outside the current scope.

Existing future bookings remain valid even if their intervals are no longer
covered by the new tariff configuration. They continue to block availability.

Room services can be added, repriced, renamed, or removed from the current
offering. Removal or renaming affects new bookings only: previously selected
services must still be provided under their recorded names and prices. Service
snapshots do not reference the current `RoomService` ID, so removed offerings can
be physically deleted without removing booking history.

Room capacity may be increased. Decreasing it is rejected while any booking has
an end later than the current UTC time, including an ongoing booking. Bookings
do not record attendance, so there is no reliable way to prove that a lower
capacity would satisfy an existing reservation.

## Room names and deletion

Room names must be unique among rooms that are not deleted, ignoring case and
leading or trailing whitespace. Enforce this in persistence with a unique index
over normalized names filtered to non-deleted rooms, not only an application
check.

Deleting a room is a soft-delete: retain its identity and booking history, exclude
it from availability search, and reject new bookings for it. Reject deletion
while any booking ends later than the current UTC time. A room with only completed
bookings may be deleted, and its history remains available to reports.

A deleted room's name may be reused by a new room with a different ID. Reports
identify and group rooms by ID rather than merging rooms with the same name.
Soft-delete is required for rooms; it is not a blanket policy for every model.
