# Q1: Reports and assignment completion

Approved by the maintainer on 2026-09-17, including the four decisions below
and execution of the plan. Start with branch/documentation preparation, then
implement and verify the reports. The [roadmap](../implementation-roadmap.md)
records delivery status; this document records the contract and actual evidence.

Status: In progress; branch and documentation preparation are complete.

Read with [CONTRIBUTING](../../CONTRIBUTING.md), the
[agent workflow](../agent-workflow.md), [domain model](../domain-model.md), and
[booking and pricing rules](../booking-and-pricing-rules.md). Preserve the
original [technical assignment](../task-specification.md).

## 1. Outcome and agreed scope

Deliver two read-only reports through the existing API/Application/Infrastructure
structure. Use persisted booking snapshots and PostgreSQL aggregation. Reuse
the current DI, timestamp parser, safe ProblemDetails mapping, and test fixtures.
One application service and one reader contract with two methods are sufficient.
No new package, Domain change, or schema migration is initially required.

| Decision | Approved Q1 behavior |
| --- | --- |
| Service identity | Group by the exact recorded service name across rooms. Comparison is case-sensitive; Projector and projector are separate. Do not normalize or merge renamed offerings. |
| Room rows | Include only rooms with bookings selected by the reporting period, including soft-deleted rooms. |
| Result size | Return all grouped rows without pagination or truncation. The number of response rows is unbounded. |
| Duration | Return totalBookedSeconds as decimal seconds, preserving whole-microsecond precision without rounding to hours. |

The scope is compact reporting and assignment verification. Occupancy, payments,
extra filters, exports, configurable sorting, background aggregation, and the
separate readability/error-result refactor are not part of Q1.

## 2. Period and calculation semantics

Both reports require start and end in the existing explicit-offset ISO 8601
profile, normalized to UTC with whole-microsecond precision. Reuse ApiTimestamp
so missing offsets and nonzero sub-microsecond digits remain invalid; additional
fractional zeros are accepted consistently with booking/search. Encode a positive
offset's plus sign as %2B in a query string.

Validate the reporting period in Application before database reads: endpoints
must be UTC, have whole-microsecond precision, and satisfy end > start. Do not
reuse RentalPeriod, which imposes booking duration bounds. Past and future
periods are valid, and Q1 adds no maximum reporting window, past-start check,
clock dependency, or tariff-coverage requirement.

Select bookings with StartsAtUtc >= start and StartsAtUtc < end. Include every
selected booking in full even if its end lies beyond the reporting window.
Bookings starting before the window are excluded even when they overlap it;
CreatedAtUtc does not determine inclusion. Values describe booked business,
not payments received or recognized revenue.

For the room report:

- Group by RoomId and display the retained room record's Name. It is not a
  snapshot of the name at booking time. Reused names on distinct IDs stay separate.
- Count each selected booking once. Sum its full EndsAtUtc - StartsAtUtc once
  as decimal seconds, including fractional seconds at microsecond precision.
- Sum recorded BookingPriceSegment.Price for rentalValue and recorded
  ServicePriceSnapshot for serviceValue. A booking without services contributes
  zero service value and still contributes its count, duration, and rental.
- totalValue is rentalValue + serviceValue, consistent with the existing Domain
  invariant for saved Booking.TotalPrice. Do not recalculate prices from rates,
  multipliers, elapsed hours, or current offerings.

For service popularity, count each selected service snapshot once and sum its
recorded price, grouping by exact ServiceNameSnapshot across the selected
bookings. An unselected current service has no row. Current service removal,
renaming, repricing, tariff changes, or room deletion cannot erase these values.

Use decimal for money and duration and 64-bit integer counts. Monetary sums
retain the recorded precision of up to three decimal places; do not round again.
Exact service comparison and name tie-breaking use PostgreSQL C collation,
independent of the environment's default linguistic collation.

## 3. HTTP contract

```http
GET /reports/rooms?start=2030-01-01T00:00:00Z&end=2030-02-01T00:00:00Z
GET /reports/services?start=2030-01-01T00:00:00Z&end=2030-02-01T00:00:00Z
```

Both operations return 200 with start and end in UTC, currency set to UAH, and
items. No results returns the same envelope with an empty array. JSON numeric
trailing-zero formatting is not part of the contract.

| Report | Item fields | Fixed ordering |
| --- | --- | --- |
| Rooms | roomId, roomName, bookingCount, totalBookedSeconds, rentalValue, serviceValue, totalValue | totalValue descending, then roomId ascending. |
| Services | serviceName, selectionCount, totalValue | selectionCount descending, then exact serviceName ascending. |

Example room response after one seed Room A booking from 11:00 to 15:00 UTC
with Projector and Wi-Fi; the ID is illustrative:

```json
{
  "start": "2030-01-01T00:00:00Z",
  "end": "2030-02-01T00:00:00Z",
  "currency": "UAH",
  "items": [
    {
      "roomId": "11111111-1111-4111-8111-111111111111",
      "roomName": "Room A",
      "bookingCount": 1,
      "totalBookedSeconds": 14400,
      "rentalValue": 8600,
      "serviceValue": 800,
      "totalValue": 9400
    }
  ]
}
```

The service report then contains Projector (selectionCount 1, totalValue 500)
and Wi-Fi (selectionCount 1, totalValue 300), in that order.

Use the existing ProblemDetails shape with code and traceId:

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | invalid_request | Missing/empty timestamps or malformed format, offset, or precision. |
| 400 | invalid_report_period | A validly parsed period is not ordered; Application also rejects non-UTC or sub-microsecond direct inputs. |
| 503 | report_persistence_unavailable | A recognized temporary persistence failure or timeout. |
| 500 | internal_error | An unexpected failure, without SQL, provider messages, or stack traces in the response. |

Use Cache-Control: no-store for report responses. Document input requirements,
selection semantics, all response fields and units, ordering, empty results,
error statuses, and the unpaginated result in OpenAPI/Swagger.

## 4. Query execution and consistency

Infrastructure owns one parameterized SQL statement per report. Prefer readable
EF projections where translation remains clear; parameterized SQL through the
existing EF context is acceptable for the aggregate query. Do not add another
data-access library or a generic reporting framework.

Filter and aggregate in PostgreSQL and materialize only the final report rows.
Aggregate price segments and selected services independently per booking before
combining their totals with booking count/duration and grouping by room. Never
join both raw child collections and sum multiplied rows. A service-free booking
must survive the room query. The service report needs only selected bookings
and their service snapshots; it does not consult current services or tariffs.

Do not load booking histories into aggregates, make one query per room, or add
speculative indexes. Reuse the existing context factory and failure classifier.
Pass cancellation through to commands; retain existing command timeouts and
avoid automatic retries. Caller cancellation is not persistence unavailability.

Each response observes one statement snapshot without explicit transactions or
room write locks. Separate report requests can observe different committed
states; no shared cross-request snapshot or reporting session is promised.

## 5. Acceptance and verification

Use the existing Application and real PostgreSQL/HTTP test projects. Add tests
for observable report behavior, not duplicate wrappers or private implementation.

| Area | Required evidence |
| --- | --- |
| Selection | Start inclusion/end exclusion, exclusion of earlier overlapping bookings, inclusion of full bookings ending outside the period, and independence from CreatedAtUtc. |
| Period input | Past/future and windows outside booking duration bounds, equivalent offsets, microseconds, rejected malformed/absent timestamps, and validation before reads. |
| Arithmetic | Multiple bookings, multiple segments and services without join multiplication, no-service bookings, fractional money, and exact fractional-second duration. |
| History | Later room/service/rule edits preserve values; removed/renamed services retain their recorded groups; deleted rooms remain reportable. |
| Identity and ordering | Same room name on distinct IDs, same service name across rooms, case-distinct service names, fixed sorting and tie-breaking. |
| Empty/complete results | Empty envelopes, omission of zero-booking rooms and unselected services, and no implicit page-size truncation. |
| Execution | One data statement per report, aggregation in PostgreSQL, cancellation, recognized safe 503 and unexpected safe 500, without retry. |
| HTTP discovery | Routes, parameters, schemas, errors and semantics documented in OpenAPI; existing operations remain valid. |
| Assignment path | Real DemoInitializer data, availability search, HTTP booking, and both reports agree on Room A's 8600 rental + 800 services = 9400 UAH example. |

The assignment test uses the real seed, not a hand-copied replacement. Existing
seed and pricing tests retain coverage of all three rooms and four tariffs;
do not repeat P1's entire infrastructure acceptance matrix.

Final commands from the repository root, using the documented local Docker setup:

```powershell
dotnet tool restore
dotnet restore Confera.slnx
dotnet build Confera.slnx --configuration Release --no-restore
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/q1/domain
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/q1/application
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/q1/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/q1/apphost
git diff --check
```

Also verify EF model consistency with the documented design-time connection,
local Markdown links/anchors, solution item paths, and the complete diff. Record
actual outcomes and unavailable checks. Documentation preparation checks are
not runtime acceptance evidence. Verified requires relevant checks to pass;
Done additionally requires confirmed merge and the roadmap's final status update.

## 6. Ordered implementation

1. Fetch origin, verify completed prerequisites, and create
   feature/booking-reports with --no-track from origin/main. Save this plan;
   move detailed task documents and related evidence to docs/plans, rebase links,
   and match the physical structure in Confera.slnx.
2. Add Application report input validation, result DTOs, one service, and a
   two-method reader contract. Implement the two Infrastructure aggregates and
   their focused Application/PostgreSQL tests.
3. Add the two GET endpoints, DI, safe error mapping, and HTTP/OpenAPI tests,
   including the real-seed assignment path.
4. Update README, current domain descriptions, local-development and .http
   examples, and Q1 roadmap progress/evidence. Run final verification and review.

Execution is authorized within this contract, including routine private naming
and implementation choices. Material scope changes still need discussion.
Publication, PR creation, and merge require separate user instructions.

## 7. Optional future upgrades

These alternatives were considered and intentionally not selected for Q1.
They are possible future tasks, not commitments, bugs, outstanding decisions,
or additional Q1 completion criteria. Reconsider them only with a concrete need
and a separately approved contract.

| Possible upgrade | Reason to reconsider | Decision still needed for that future task |
| --- | --- | --- |
| Normalize service-name grouping | A business wants case variants consolidated. | Normalization rules, display name selection, and changed historical grouping; renamed services still have no shared catalog identity. |
| Include active rooms with zero bookings | A business wants to identify rooms without demand in a period. | Active-room population, historical lifecycle semantics, and treatment of deleted rooms without selected bookings. |
| Paginate reports like availability search | Group counts make complete responses impractical. | page/pageSize/hasNextPage contract, limits, and behavior across concurrent changes. |
| Display duration in hours | Consumers need ready-to-display hour totals. | Precision and rounding rules, preferably preserving exact seconds alongside the display value. |

## 8. Preparation and validation record

Preparation on 2026-09-17: origin/main and local main were both d851fdc, including
A1's merge 85988e8 and the B1/R1/H1 prerequisites. The initial working tree was
clean. Created feature/booking-reports with --no-track; it has no upstream.

Preparation checks passed: Release build with zero warnings/errors, 95 local
links/anchors, 37 solution paths, all 16 Markdown registrations and their solution
folders, and diff whitespace. Runtime tests were not run for this documentation
stage; these checks are not report acceptance evidence. Implementation follows
under the existing approval.
