# A1: Availability search specification and implementation plan

The maintainer approved the plan and decisions on 2026-09-16, but explicitly
authorized only creation of the feature branch and this specification first.
**Implementation requires a separate, explicit maintainer confirmation after
reviewing this document.** Plan approval and documentation validation do not
authorize application code, new tests, or runtime configuration changes yet.

This document records the agreed contract in English, following the B1/R1 plans.
Delivery status belongs in the [roadmap](implementation-roadmap.md); actual
validation evidence belongs in the record below. Read alongside
[CONTRIBUTING](../CONTRIBUTING.md), the [agent workflow](agent-workflow.md),
[domain model](domain-model.md), and
[booking and pricing rules](booking-and-pricing-rules.md). Preserve the original
[technical assignment](task-specification.md).

## 1. Outcome and scope

Deliver `GET /rooms/availability` with a requested period, minimum capacity,
and simple page/pageSize pagination. Return active, sufficiently large rooms
without overlapping bookings, provided tariffs cover the entire interval.
Include current room-specific services so a client can select their IDs for
`POST /bookings`. This supplies the remaining core business operation of H1.

Decisions resolved with the maintainer:

| Topic | Agreed behavior |
| --- | --- |
| Room information | ID, name, capacity, base hourly rate, UAH currency, and current service IDs/names/prices. |
| Period price | No estimated rental total or tariff breakdown in search; booking calculates the confirmed price. |
| Missing tariff coverage | Successful search with an empty result, including an empty tariff set. |
| Pagination | page/pageSize with items, page, pageSize, and hasNextPage; no totalCount. |
| Defaults and limits | page defaults to 1; pageSize defaults to 20 and must be 1-100. Invalid values return 400. |
| Ordering | Capacity ascending, then room ID ascending; current services ordered by ID. |
| Empty page | 200 with empty items and hasNextPage false, including a page beyond the available results. |
| Availability guarantee | Search does not reserve a room; booking rechecks current state. |

Keep this technical assignment focused. Use the existing layers, EF Core,
PostgreSQL, TimeProvider, error handling, and test infrastructure. No new
packages, tables, migrations, generic repositories, mediator pipelines, search
framework, caching infrastructure, or additional API operations are planned.
Do not add a separate opening-hours model, tariff versions, price-quote API,
service filters, configurable sort modes, or reports under A1.

## 2. Existing foundation and affected components

P1, B1, and R1 are merged prerequisites. Existing reusable behavior includes
BookingValidation, BookingPriceCalculator, room/service response fields,
the booking overlap predicate, provider-error classification, controlled test
clocks, and the isolated PostgreSQL/HTTP test fixtures.

| Component | Intended change after implementation confirmation |
| --- | --- |
| Domain | Reuse period validation and extract a small shared tariff-coverage mechanism from the existing calculator. |
| Application | One availability use case, search input/page result, and a focused read contract. |
| Infrastructure | Read the full tariff set and query a filtered room page with current services; translate recognized persistence failures. |
| API | GET endpoint, query validation, shared timestamp parsing, DI, safe errors, and OpenAPI metadata. |
| Tests | Focused Domain/Application behavior, PostgreSQL query/page behavior, and HTTP contracts using existing fixtures. |
| Documentation | This specification, README, current domain/rule descriptions, local-development examples, HTTP examples, and A1/H1 roadmap evidence. |

Application owns orchestration and its persistence contract. EF Core/Npgsql
and database queries stay in Infrastructure. Controllers call the use case.
Keep Domain independent of HTTP and persistence; do not add booking history
to Room or reuse the booking write transaction for search.

## 3. HTTP contract

### Request and validation

```http
GET /rooms/availability?start=2030-01-15T11:00:00Z&end=2030-01-15T15:00:00Z&capacity=50&page=1&pageSize=20
```

| Query parameter | Rule |
| --- | --- |
| start | Required ISO 8601 timestamp with Z or an explicit UTC offset. |
| end | Required timestamp with the same format and precision rules. |
| capacity | Required positive 32-bit integer; room capacity must be at least this value. |
| page | Optional positive 32-bit integer, starting at 1; default 1. |
| pageSize | Optional integer in 1-100 inclusive; default 20. |

Normalize timestamps to UTC before Application. Validate their original text
before parsing can discard sub-microsecond digits. Extra fractional zeros are
allowed when they do not change the represented precision. A nonzero digit
beyond six fractional places is invalid. In URLs, encode a positive offset's
plus sign as `%2B`, for example `2030-01-15T13:00:00%2B02:00`.

Missing/empty required parameters, malformed timestamps or numbers, nonpositive
capacity/page, and an out-of-range pageSize return 400. Optional pagination
defaults apply when the parameter is omitted. Do not silently clamp invalid
values. Validate pagination arithmetic before querying; an offset outside the
supported query integer range returns 400 instead of wrapping or causing 500.

Reuse the booking period rules: ordered endpoints, whole-microsecond precision,
30 minutes to 24 hours inclusive, and start not before the captured current UTC
instant. The duration need not be a multiple of 30 minutes. There is no new
advance-booking horizon. Capture one TimeProvider value, floor it to whole
microseconds, and validate before database reads. Search need not revalidate
against a second clock reading at response time; booking validates again.

The existing JSON timestamp converter alone does not validate query strings.
Share its strict parsing rules with query input through a small API helper;
preserve the accepted B1 timestamp formats, precision behavior, and error codes.
Keep the literal availability route compatible with GET /rooms/{id}.

### Success

Return 200 with the following shape. IDs below are illustrative, not seed IDs:

```json
{
  "items": [
    {
      "id": "11111111-1111-4111-8111-111111111111",
      "name": "Room A",
      "capacity": 50,
      "hourlyRate": 2000.000,
      "currency": "UAH",
      "services": [
        {
          "id": "22222222-2222-4222-8222-222222222222",
          "name": "Projector",
          "price": 500.000
        }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "hasNextPage": false
}
```

Reuse the existing RoomResult/RoomServiceResult shape where practical. Prices
are JSON numbers; fixed trailing-zero formatting is not part of the contract.
The hourly rate is the base rate, not a tariff-adjusted period quote. Each
service price is its current once-per-booking price. Rooms with no services
remain eligible and return an empty services array.

Echo the effective page and pageSize. Return at most pageSize items. Missing
tariff coverage, no matching rooms, and a page beyond the result all return the
same empty envelope:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "hasNextPage": false
}
```

The requested page is preserved for an out-of-range page; the example above
uses page 1. No total count, total page count, booking history, rental segments,
or room version is added. Use Cache-Control: no-store for this current-state
search. Room editing continues to obtain its ETag from GET /rooms/{id}.

### Errors and OpenAPI

Use the existing ProblemDetails shape with code and traceId:

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | invalid_request | Missing/malformed query input, invalid capacity or pagination, or unsupported pagination offset. |
| 400 | invalid_booking_period | Reused Domain rejection of the period, including a past start or duration outside the bounds. |
| 503 | availability_persistence_unavailable | Recognized temporary database failure or timeout. |
| 500 | internal_error | Unexpected internal failure or invalid tariff configuration. |

No results is not 404 or 409. Missing tariff coverage is an empty successful
search, while POST /bookings retains its existing tariff_coverage_missing 400.
A malformed tariff configuration, such as duplicate priorities, remains an
internal failure rather than being treated as ordinary missing coverage.
Never catch every ArgumentException as invalid input or every failure as an
empty result. Do not expose SQL, provider messages, or stack traces.

Document the query fields, defaults/ranges, response envelope, error statuses,
UTC/precision, ordering, and availability limitations in OpenAPI/Swagger.

## 4. Domain reuse and query execution

### Shared tariff interpretation

Reuse BookingValidation for the rental period and past-start rule. Extract
only the small common coverage/interval logic needed by search and the current
BookingPriceCalculator. Preserve daily expansion, previous-day occurrences of
overnight rules, clipping, full coverage, and complete-set priority validation.
Do not write a second tariff algorithm or use a dummy room/hourly rate merely
to check coverage.

The calculator must retain priority selection, every applicable boundary,
per-segment rounding, and confirmed snapshot behavior. This extraction is not
a pricing redesign. Existing pricing and booking tests remain regression
coverage; add focused checks for the shared coverage entry point.

### Read sequence

1. Validate input and cancellation, and capture the canonical clock value.
2. Read the entire global tariff set once and check coverage in Application
   through Domain. No tariff query per room.
3. If coverage is absent, return the empty envelope without querying room pages.
4. Query eligible rooms in PostgreSQL with all of these conditions:
   - IsDeleted is false.
   - Capacity is greater than or equal to the requested capacity.
   - No booking for the room satisfies
     `StartsAtUtc < requestedEnd && EndsAtUtc > requestedStart`.
5. Order by Capacity and then Id ascending. Apply offset
   `(page - 1) * pageSize` and fetch at most `pageSize + 1` rooms.
6. Read those rooms and their current services in the same SQL statement.
   Derive hasNextPage from the extra room, discard that extra item, and return
   the page. Order services by ID as in the existing room response.

Use a no-tracking read/projection. Pagination applies to rooms before expanding
their service collection; services must not consume page slots or duplicate
rooms in the response. The overlap check is a database existence predicate,
not a loaded booking collection. Do not materialize all candidate rooms before
paging, issue per-room database calls, or run a count query.

The normal covered search requires two data queries: tariffs, then the room
page with services and overlap filtering. The uncovered path needs only the
tariff query. Reuse current indexes initially; no speculative schema changes
or separate performance benchmark project are required for this assignment.

### Consistency, cancellation, and failures

Search is read-only and acquires no room-row write locks. No explicit transaction
is needed across its two reads. The room page, services, and overlap predicate
share one statement snapshot. Tariffs are the set read earlier in the request;
there is no common snapshot guarantee across tariffs and rooms, and no guarantee
that they remain unchanged by response time.

Each page request is a fresh search. Ordering is deterministic for unchanged
data; concurrent changes can shift results between page requests. hasNextPage
describes the page query's observed state. There is no pagination session,
cross-request snapshot, or reservation token.

B1 remains responsible for locking, reading current room/services/tariffs,
revalidating the period and availability, and preserving overlap integrity.
A room found by search can subsequently become unavailable or have different
service offerings/prices. Search does not change B1/R1 write semantics.

Pass cancellation to database commands and use existing command timeouts.
Classify recognized temporary failures through the existing infrastructure
helper and expose a safe read-specific 503. Do not replay the search
automatically or translate caller cancellation into database unavailability.

## 5. Acceptance and verification

Use the existing test projects, isolated PostgreSQL fixture, real migrations,
WebApplicationFactory, and controlled TimeProvider. Add meaningful tests with
implementation; avoid duplicating the full pricing matrix through HTTP or
repeating the P1 infrastructure acceptance matrix.

| Area | Required evidence |
| --- | --- |
| Capacity and lifecycle | Exact/larger capacity included; smaller capacity and deleted rooms excluded; rooms without services remain eligible. |
| Overlap | Partial overlaps, containment, and equal intervals excluded; both adjacency directions allowed; bookings of another room do not block this room. |
| Coverage | Full coverage succeeds; missing beginning, middle, end, and empty tariffs give an empty result; overnight rules work. |
| Shared Domain behavior | Search and booking agree on period/coverage; invalid tariff configuration stays an internal failure; existing pricing/rounding/snapshot tests pass. |
| Time input | Equivalent Z/offset instants, microsecond precision and trailing zeros, missing offsets and nonzero sub-microsecond digits, past starts, ordered endpoints, and duration bounds. |
| Page contract | Defaults, first/middle/last/beyond-last pages, pageSize bounds, invalid numbers/offset overflow, hasNextPage at exactly pageSize and pageSize + 1 matches. |
| Stable ordering | Multiple equal-capacity rooms use ID order; unchanged data pages do not duplicate or omit rooms; multiple services do not consume room slots. |
| Current offerings | Committed R1 rate/service changes appear in later searches; returned service IDs are usable by B1; removed offerings disappear. |
| No reservation | Search finds a room, another client books the period, and the original client's booking gets the existing conflict. |
| Query shape | Database filtering/paging, bounded query count independent of room count, no booking-history loading, no count query, and no room query for missing coverage. |
| Safe failures | Invalid input returns 400; recognized temporary failures return 503; unexpected errors return safe 500; cancellation propagates. |
| HTTP discovery | Endpoint, required parameters, defaults, ranges, response schema, and statuses appear in OpenAPI; existing room-by-ID and booking routes still work. |

Verify query behavior with the existing EF interception/test patterns and real
PostgreSQL. Do not rely only on in-memory collections or exact generated SQL
text. Coordinate any concurrent test with observable events rather than sleeps.

Final implementation checks follow CONTRIBUTING:

```powershell
dotnet tool restore
dotnet restore Confera.slnx
dotnet build Confera.slnx --configuration Release --no-restore
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/a1/domain
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/a1/application
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/a1/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/a1/apphost
git diff --check
```

Check that EF reports no pending model changes using the documented design-time
connection procedure, and verify documentation links, solution items, and the
full diff. Record actual commands/results and material unavailable checks.

## 6. Ordered implementation and documentation

1. Create the feature branch and save this specification, with README/roadmap
   links and solution registration. This is the currently authorized stage.
2. Obtain the maintainer's separate confirmation to start full implementation.
3. Add shared Domain coverage behavior and its regression checks.
4. Add the Application search contract/use case and the Infrastructure read
   implementation, with period, coverage, filtering, and pagination tests.
5. Add the endpoint, query timestamp handling, DI, safe errors, and HTTP/OpenAPI
   tests while preserving B1/R1 contracts.
6. Update README, domain/rule descriptions, local-development and .http examples
   to show search followed by booking with returned IDs. Record actual A1/H1
   progress and verification evidence in the roadmap and this document.
7. Run the final checks, review all changes, and hand off the verified result.

Continue on feature/availability-search when implementation is authorized.
No material business decision remains unresolved. Routine private helper/type
names may be chosen during implementation; new material scope, contract, or
architecture changes require discussion under the agent workflow.

A1 remains Planned during this documentation stage. After implementation starts
it becomes In progress, then Verified only when the contract and relevant checks
pass. H1 can become Verified when its complete operation set is verified. Done
requires confirmed merge and the final roadmap update under CONTRIBUTING.
Q1 reports remain separate. This request does not authorize committing, pushing,
creating a PR, or merging.

## 7. Preparation and validation record

Preparation on 2026-09-16: fetched origin and confirmed R1 commit 5ce2eb6 on
origin/main. Created feature/availability-search from 39ca4cc with --no-track;
the branch has no upstream. The initial working tree was clean.

Only this specification, README/roadmap references, and solution registration
are part of the preparation change. A1 runtime behavior and tests have not been
implemented. Existing B1/R1 test evidence is not A1 acceptance evidence.

Documentation-stage validation on 2026-09-16:

| Check | Result |
| --- | --- |
| Branch prerequisites | Fetched origin/main at 39ca4cc; merged R1 prerequisite confirmed; feature branch has no upstream. |
| Release build | `dotnet build Confera.slnx --configuration Release --no-restore` passed with zero warnings/errors. |
| Documentation and solution | 80 local links/anchors, 36 solution paths, and all 15 Markdown registrations verified. |
| Diff review | Only the specification, README, roadmap, and solution registration changed; tracked and new-document whitespace checks passed. |
| Runtime tests | Not run for this documentation-only change; A1 acceptance tests remain future implementation work. |

Preparation is complete. A1 remains Planned and awaits separate implementation
confirmation; no commit, push, PR, or merge was performed.
