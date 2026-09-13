# Booking and pricing decisions

These are project decisions used to interpret the
[technical assignment](task-specification.md). The calculation is not yet
implemented; this document defines its intended behavior.

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
- Bookings are not restricted to a single calendar day or a fixed 24-hour maximum.
  Coverage is checked across the entire interval, including every affected date.
- Booking timestamps are UTC. Daily tariff hours are interpreted in the business
  time zone, whose concrete configuration is still to be decided.

For example, a 04:00–14:00 booking is rejected by the initial configuration because
04:00–06:00 has no tariff coverage. A later night rule can make that interval
bookable without changing the coverage requirement.

## Segments and rule selection

Split the booking at every applicable rule boundary. For each resulting segment,
select the covering rule with the highest `Priority`. Peak pricing takes
precedence over standard pricing; multipliers are not combined.

If multiple covering rules share the highest priority, reject the calculation as
ambiguous. Segment duration must be positive. The resulting segments must cover
the booking completely, stay within its boundaries, and have no gaps or overlaps.

For an 11:00–15:00 booking, the intended segments are:

| Segment | Tariff | Multiplier |
| --- | --- | --- |
| 11:00–12:00 | Standard | 1.00 |
| 12:00–14:00 | Peak | 1.15 |
| 14:00–15:00 | Standard | 1.00 |

## Price and service snapshots

- Segment price is elapsed duration in hours multiplied by the room's hourly
  rate and the selected rule's multiplier. Fractional hours are charged
  proportionally rather than rounded up to whole hours.
- Elapsed duration is calculated between UTC instants after resolving local
  tariff boundaries.
- Each selected service is charged once per booking using the room's current
  price. Unknown or duplicate service IDs are rejected.
- The total equals the sum of the recorded segment prices and service prices.
  The monetary rounding policy must be defined before implementation so that
  the stored breakdown and total agree.
- Record the room hourly rate, selected service names and prices, and segment
  tariff codes and multipliers as snapshots. Later configuration changes must
  not recalculate confirmed booking prices.

## Open decisions

- The business time zone and handling of ambiguous or nonexistent local times
  during daylight-saving transitions.
- Monetary precision, rounding mode, and the treatment of segment prices that
  round to zero.
- When changes to pricing rules take effect and how they affect bookable hours
  for existing future bookings.
- Room deletion when historical or future bookings exist.

Availability checks and database protection against concurrent overlapping
bookings belong to the application and persistence work that follows this domain
foundation.
