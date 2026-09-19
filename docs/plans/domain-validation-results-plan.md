# RF2: Expected Domain validation failures

The maintainer approved this plan and implementation on 2026-09-18 after review
of RF1. Work starts on `refactor/domain-validation-results`, created with
`--no-track` from fetched `origin/main` at `85cb21a`. RF1 was merged through
PR #12 as `f777e46`; its historical plan and validation remain intact.

## Approved scope and contracts

Use synchronous `Try...` methods with nullable-flow annotations and Domain-owned
typed failures. Application retains its RF1 result/feature-error contracts and
maps Domain failures explicitly. Domain gains no dependency or result library.
Return the first failure, preserving existing validation order and messages.

- `RentalPeriod.TryCreate` validates ordered UTC microsecond endpoints and the
  inclusive 30-minute to 24-hour duration.
- Booking validation exposes past-start and service-selection failures without
  exceptions. A malformed application clock remains an internal error.
- `Room.TryBook` and `BookingPriceCalculator.TryCalculate` expose invalid service
  selection and missing coverage. `HasFullCoverage` retains its boolean contract.
- `RoomDetails.TryCreate` validates and captures the complete room replacement
  before any aggregate mutation. Shared primitive rules also serve the existing
  throwing guards; there is one implementation of each rule.
- Booking, availability, and room create/update use the non-throwing contracts.
  Remove their Domain-validation exception adapters, retaining persistence
  adapters around acquisition, work, commit, and asynchronous disposal.
- Keep throwing factories/methods for compatible direct callers, backed by the
  same non-throwing rules. They are not used for expected request rejection.

Invalid tariff configuration, null validated objects/configuration, broken
snapshot/segment invariants, and programming mistakes still throw. Raw room
setters, tariff configuration APIs, report contracts, success DTOs, controllers,
schema, dependencies, retries, and unrelated cleanup are outside this refactor.
Shared guard implementations may change without changing those callers' rules.

## Compatibility requirements

Booking order remains room ID, rental period, service-ID structure, capture of
caller input, transaction/room lock, room existence, tariff read, clock and past
start, overlap, service ownership, and tariff coverage. Read the clock only once
after the lock, and use that canonical UTC instant throughout the booking.

Room input order remains name, capacity, hourly rate, then each service's name
and price, then normalized-name duplicates. Validate/capture before the first
await. Lifecycle conflicts precede version checks. Rejected replacements leave
all fields, service identities, and Version unchanged; no-op replacements and
bookings retain Version. Preserve Unicode normalization and price precision.

Missing booking coverage remains 400 `tariff_coverage_missing`; missing search
coverage remains a successful empty page without a room query. Invalid tariff
configuration remains internal. Keep segment boundaries, priority selection,
rounding, snapshots, totals, query counts, locks, and transaction behavior.

Keep RF1 statuses, codes, exact safe details, ProblemDetails metadata/cache
headers, ETag/If-Match precedence, automatic model validation, and OpenAPI.
Characterize room details previously derived from ArgumentException.Message,
including parameter suffixes, before replacing that mechanism. Do not create
exceptions to format ordinary failure descriptions. Unexpected failures from
caller-supplied enumerators are not validation failures.

Cancellation must not become an expected Domain rejection. Technical failures
retain their original Infrastructure logging owner, cause, and request scope.
Disposal failures can override pending rejection/success; do not return success
before persistence completes or add automatic replay.

## Implementation and verification

1. Register this plan and RF2, then characterize messages and mixed-input order.
2. Add shared rules and typed failures; migrate period, booking/pricing, and search.
3. Migrate room input construction and Application room operations.
4. Exercise new result paths, compatible wrappers, atomicity, input capture,
   cancellation races, and transaction cleanup. Retain existing business tests.
5. Update README, CONTRIBUTING, domain model, and roadmap to describe actual flow.
6. Build Release and run all four implemented suites with TRX output under
   `artifacts/tests/rf2`. Check EF model consistency, HTTP/OpenAPI, documentation
   links and solution registration, scoped formatting, and `git diff --check`.

RF2 is Verified only after its criteria pass; Done requires confirmed merge.
Branch publication, a PR, and merge require separate instructions.

## Validation record

Verified locally on Windows, 2026-09-18. RF2 remains unmerged on the task branch.

Delivered contracts:

- Booking Try methods return `BookingValidationFailure` with the existing
  `BookingValidationError` categories and safe descriptions. RoomDetails returns
  `RoomValidationFailure` with typed room-field/service categories. A failed
  factory/calculation returns no value; successful calls return no failure.
- Shared internal InputFailure data preserves guard descriptions, parameter
  suffixes, and exception kinds for throwing wrappers. Expected paths construct
  no exception. Primitive numerical limits, precision, text normalization, and
  interval rules have one implementation shared with guards.
- Application explicitly maps these failures, checks cancellation before
  returning Domain rejections, and retains full-scope persistence adapters.
  Controllers, public HTTP DTOs, provider code, and model mappings are unchanged.
- Existing wrappers remain covered alongside the new paths. Unexpected caller
  enumeration failures propagate, rather than being caught as invalid room data.

Before production changes, 12 Domain cases and nine real HTTP cases passed on
the RF1 implementation, fixing the expected room detail messages and mixed-input
precedence. The same cases pass against the non-throwing implementation. Overall,
RF2 adds 31 Domain, 16 Application, and nine Integration cases; existing coverage
also exercises the new APIs. Coverage includes no partial values/mutations,
clock precision, input capture, cancellation races, cleanup overriding a Domain
rejection, and configuration defects remaining exceptional.

Commands (Release):

```powershell
dotnet build Confera.slnx --configuration Release --no-restore
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --filter-method '*ValidationDetails*' --report-trx --results-directory artifacts/tests/rf2/baseline-domain
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --filter-method '*ValidationDetails*' --report-trx --results-directory artifacts/tests/rf2/baseline-http
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/rf2/domain-final
dotnet test --project tests/Confera.Application.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/rf2/application
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/rf2/integration
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build --report-trx --results-directory artifacts/tests/rf2/apphost
$env:ConnectionStrings__confera = 'Host=localhost;Database=design_time_only'
dotnet ef migrations has-pending-model-changes --project src/Confera.Infrastructure --configuration Release --no-build
git diff --check
```

The filtered baseline commands preceded the production refactor; the unfiltered
runs above validate the implementation. AppHost used normal Docker context
discovery, without a named-pipe override, and passed in approximately 3.5 minutes.

| Check | Result |
| --- | --- |
| Release build | Passed; zero warnings/errors. |
| Domain | 142 passed. |
| Application | 85 passed. |
| Integration / PostgreSQL / HTTP / OpenAPI | 161 passed. |
| AppHost / MigrationWorker | 12 passed. |
| EF model | No changes since the last migration. |
| Documentation | 115 local links/anchors, 39 solution paths, all 18 Markdown registrations and RF2 solution folder passed. |
| Source and whitespace | Changed/new sources reviewed; scoped dotnet format whitespace, new-file whitespace checks, and git diff --check passed. |

All 400 tests passed in the final suite runs, with no failures or skips. The first
Domain run had one new assertion expecting ArgumentException exactly where the
existing hourly-rate guard correctly throws ArgumentOutOfRangeException. The
assertion was corrected without changing runtime behavior; the full Domain suite
then passed. That earlier failed TRX is retained under `artifacts/tests/rf2/domain`.

TRX evidence remains ignored under `artifacts/tests/rf2`. Hosted Linux CI was not
run. No schema, package, project-reference, HTTP-contract, or business-rule change
was introduced. After merge and confirmation of relevant checks, the remaining
handoff is the roadmap's final Done update under CONTRIBUTING.md.
