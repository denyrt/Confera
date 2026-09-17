# RF1: Readability and explicit operation results

The maintainer accepted the recommendations below on 2026-09-18 and authorized
creating `feature/readability-refactoring`, preparing documentation as its first
changes, and making one local documentation commit. **Implementation requires
a subsequent explicit instruction from the maintainer.** This approval does
not authorize a push, pull request, or merge.

RF1 remains Planned in the [roadmap](../implementation-roadmap.md). The design
is agreed; the code examples describe intended structure and are not implemented
or compiled acceptance evidence. Later implementation must continue on this
task branch, subject to the maintainer's instructions.

## 1. Outcome and current problem

Make expected operation outcomes visible in Application signatures and make
their HTTP mapping visible when reading the corresponding controller. Preserve
the existing observable behavior while improving the path a reader follows.

At the preparation baseline, `origin/main` is `3f4c2f7`, containing the completed
B1, R1, A1, H1, and Q1 work. The working tree was clean before branch creation.

- Controllers normally show only success. For example,
  [BookingsController](../../src/Confera.Api/Bookings/BookingsController.cs)
  returns 201 after calling the service.
- [ApiExceptionHandler](../../src/Confera.Api/Errors/ApiExceptionHandler.cs)
  maps expected failures from all four feature areas, including Domain
  validation exceptions, to HTTP responses.
- Request helpers such as `RoomRequest.ToCommand`, `ReportRequest.ToUtc`, and
  `RoomEntityTags.Parse` can throw application exceptions before the use case
  runs. Their conversion-oriented names do not expose that failure path.
- Infrastructure already recognizes specific constraints and temporary failures.
  Application tests often assert exception types; HTTP tests protect the public
  contract and can continue to do so after internal restructuring.

The [B1 contract](b1-booking-implementation-plan.md#5-http-contract),
[R1 contract](r1-room-management-plan.md#version-and-http-preconditions),
[A1 contract](a1-availability-search-plan.md#3-http-contract), and
[Q1 contract](q1-reports-plan.md#3-http-contract) remain the behavior references.
This plan changes internal error flow; historical plans retain their original
implementation and validation records.

## 2. Accepted decisions

| Area | Decision |
| --- | --- |
| Application outcomes | Introduce a small locally owned `Result<TValue, TError>` and a result without a payload for operations such as deletion. No result-library dependency is planned. |
| Errors | Use feature-specific types without HTTP concepts. Prefer typed records where cases carry different data; an enum is sufficient for a simple set without distinct payloads. Establish the concrete style with booking first. |
| Controller mapping | Start with explicit success handling and a failure switch in the action. A private method in the same controller is acceptable when the mapping becomes unwieldy. |
| Problem response construction | Keep `ApiProblems` as the common formatter for the existing response shape. Controllers choose the HTTP meaning. |
| Unexpected failures | Retain a global handler for safe 500 responses and diagnostics. Preserve request cancellation behavior. |
| Migration depth | First make Application-to-API outcomes explicit. Known Domain and persistence exceptions may be adapted at the Application boundary during this stage. |
| Request conversion | Expose ordinary parsing or input-conversion failures through `Try...` methods or typed results. Retain the existing automatic `[ApiController]` model validation. |
| Compatibility | Preserve statuses, codes, bodies, headers, validation precedence, transaction behavior, cancellation, and the absence of automatic retries. |
| Execution | Document first, then wait for implementation authorization. Booking is the first complete implementation example; apply the evaluated approach to the other operations afterward. |

The exact private helper names and file organization are implementation details
within these decisions. Introducing a package, changing dependency direction or
public behavior, or materially expanding scope requires an explicit decision.

## 3. Result and error contracts

Application owns the result types, initially under `Confera.Application/Common`.
They represent either success with a value or failure with an error. Construction
must prevent contradictory states and missing required payloads. Use explicit
`Success` and `Failure` factories. Accessing the inactive value or error is a
programming mistake, not an expected business rejection.

The proposed service contracts include:

```csharp
Task<Result<BookingResult, BookingError>> CreateAsync(
    CreateBookingCommand command,
    CancellationToken cancellationToken = default);

Task<Result<RoomError>> DeleteAsync(
    Guid roomId,
    IReadOnlyList<Guid>? expectedVersions,
    CancellationToken cancellationToken = default);
```

Keep existing successful DTO names and wire schemas initially. A later rename
of `BookingResult` to a name such as `BookingDetails` is a separate readability
candidate: even an unchanged JSON body can accompany an OpenAPI component rename.
The result wrapper itself must never become the HTTP response payload.

An illustrative booking error model is:

```csharp
public abstract record BookingError
{
    public sealed record InvalidRequest : BookingError;
    public sealed record InvalidPeriod(string Description) : BookingError;
    public sealed record InvalidServiceSelection(string Description) : BookingError;
    public sealed record MissingTariffCoverage(string Description) : BookingError;
    public sealed record RoomNotFound : BookingError;
    public sealed record RoomUnavailable : BookingError;
    public sealed record PersistenceUnavailable : BookingError;
}
```

Validation descriptions are known safe domain/input messages. Do not copy
arbitrary provider or unexpected exception messages into an error payload.
Application errors contain no status, `ProblemDetails`, or `HttpContext`.
Domain must not acquire a dependency on Application to reuse this result type.

## 4. Visible HTTP mapping

The following action fragment illustrates all expected booking outcomes after
the proposed types and `ApiProblems.Response` helper exist:

```csharp
var result = await service.CreateAsync(command, cancellationToken);

if (result.IsSuccess)
{
    return StatusCode(StatusCodes.Status201Created, result.Value);
}

var (status, code, detail) = result.Error switch
{
    BookingError.InvalidRequest =>
        (400, "invalid_request", "Supply a nonempty room ID."),
    BookingError.InvalidPeriod error =>
        (400, "invalid_booking_period", error.Description),
    BookingError.InvalidServiceSelection error =>
        (400, "invalid_service_selection", error.Description),
    BookingError.MissingTariffCoverage error =>
        (400, "tariff_coverage_missing", error.Description),
    BookingError.RoomNotFound =>
        (404, "room_not_found", "The room was not found."),
    BookingError.RoomUnavailable =>
        (409, "room_unavailable", "The room is already booked for part of this period."),
    BookingError.PersistenceUnavailable =>
        (503, "booking_persistence_unavailable",
            "The booking result could not be confirmed. "
            + "A failed response does not prove that no booking was saved."),
    _ => throw new InvalidOperationException("The booking error has no HTTP mapping.")
};

return ApiProblems.Response(HttpContext, status, code, detail);
```

A missing mapping is an implementation defect and must remain a safe internal
failure. Ordinary C# record inheritance does not guarantee an exhaustive switch;
behavioral coverage must include every expected case.

`ApiProblems.Response` should construct a concrete `ObjectResult` from the
existing `Create` method, explicitly setting its status and
`application/problem+json` content type. Preserve `type`, `title`, `detail`,
`status`, `code`, and `traceId`. Share formatting without hiding all feature
decisions in a new universal result-to-HTTP mapper.

Preserve the automatic model-state response factory for invalid JSON, required
fields, and binding errors. Document that these requests can finish before the
action executes. Parser failures reached inside an action should be visible
there, while retaining the existing path-specific safe detail messages.

For If-Match parsing, preserve all three states:

| Input | Parsing result |
| --- | --- |
| Header absent | Successful parse with absent versions; Application evaluates the room state. |
| Malformed header or unsupported wildcard | Explicit parsing failure, mapped to 400. |
| Valid syntax without a matching strong version | Successful parse, possibly with an empty version list; Application evaluates the precondition. |

Do not return 428 immediately on an absent header: repeated deletion of an
already deleted room still succeeds. Preserve repeated header fields and strong
comparison, including the behavior of weak and unknown tags.

## 5. Application, transactions, and technical failures

Replace direct expected throws in use cases with early failure results. For
example, a missing/deleted booking room returns `BookingError.RoomNotFound`,
and a preliminary overlap returns `BookingError.RoomUnavailable`.

At the first stage, adapt only known validation and operation exceptions into
these feature errors around the complete use-case execution. The protected
scope must include transaction acquisition, work, commit, and asynchronous
disposal. Unexpected failures continue to propagate. Do not classify every
`ArgumentException` as invalid client input or every failure as an empty search.
An unexpected domain configuration/invariant failure remains an internal error.

Early failure returns must dispose the transaction and release its locks without
saving. Preserve capture of caller-owned collections before asynchronous work,
the point where the clock is read, validation order, and response preparation
before writing. Return success only after successful persistence completion;
exercise save, commit, and disposal failures as applicable rather than assuming
that returning a result bypasses cleanup behavior.

Infrastructure continues to interpret SQLSTATE and exact constraint names.
The booking exclusion violation maps to the same error as a preliminary overlap;
active room-name conflicts and version conflicts keep their meanings. A provider
exception is still a valid technical signal at this boundary. Do not require a
result check after every low-level query solely for uniformity.

Recognized temporary failures become feature-specific unavailable outcomes.
Preserve original-exception diagnostics and request/trace correlation when
moving them out of the API handler. Establish one logging owner at the technical
failure translation boundary and avoid duplicate application-level logging.
Business rejections do not become unexpected-error logs. Keep diagnostics out of
public error payloads and keep Application independent of provider/hosting types.

Caller cancellation must not become a business rejection or a persistence-503
result. Continue passing cancellation to database operations and preserve the
handler's request-aborted behavior. Preserve bounded waits and the absence of
automatic write or query replay. A failed or lost response still does not prove
that a write was not committed.

While features are migrated one at a time, retain global expected-error mappings
for the features not yet migrated. Remove a feature's mappings only when its
Application and request-parsing paths are covered. The completed boundary stage
leaves the global handler responsible for safe unexpected failures and diagnostics.

## 6. Compatibility checks

The existing contracts remain authoritative; this table highlights migration risks.

| Area | Behavior to preserve |
| --- | --- |
| Booking | 201 and its existing body without a new Location header; 400 `invalid_request`, `invalid_booking_period`, `invalid_service_selection`, `tariff_coverage_missing`; 404 `room_not_found`; 409 `room_unavailable`; 503 `booking_persistence_unavailable`. |
| Rooms | POST 201 with Location; GET/PUT 200; DELETE 204. Existing `invalid_request`, `invalid_room_data`, `room_not_found`, `room_name_conflict`, `room_has_unfinished_bookings`, `room_version_mismatch`, `room_precondition_required`, and `room_persistence_unavailable` codes with their existing statuses and messages. |
| Room headers and precedence | GET supplies ETag and no-store; PUT supplies no ETag; 428 supplies no-store. Lifecycle conflicts precede version checks. Repeated DELETE remains 204 with old/missing conditions; malformed conditions remain 400. |
| Availability | 200 with existing pagination and current services. Missing coverage is a successful empty page. Retain 400 `invalid_request` / `invalid_booking_period`, 503 `availability_persistence_unavailable`, and no-store. |
| Reports | Existing UTC/UAH envelopes, grouping, ordering, complete results, and no-store. Retain 400 `invalid_request` / `invalid_report_period` and 503 `report_persistence_unavailable`. |
| Unexpected failure | Safe 500 `internal_error`, including invalid configuration. No SQL, stack traces, or provider messages in responses. |
| Persistence and Domain | Existing locks, isolation, constraints, time precision, pricing, snapshots, query counts, and commit behavior. No schema migration or changed business rule is required. |

Preserve OpenAPI response schemas and documented statuses for every operation.
Before migration, inventory relevant detail messages and mixed-invalid-input
precedence from the code and tests; do not replace these with a generic message
or reorder guards as incidental cleanup.

## 7. Ordered work and authorization

| Unit | Scope | Current authorization |
| --- | --- | --- |
| 0. Preparation | Create the task branch, write this plan, register it in README/solution/roadmap, validate documentation, and make the first local commit. | Authorized on 2026-09-18. |
| 1. Booking example | Add the minimal result contracts and booking errors, migrate the booking service and controller, preserve diagnostics, and adapt relevant tests. | Awaiting implementation instruction. |
| 2. Room operations | Apply the evaluated approach to room CRUD, explicit request/If-Match parsing, no-payload results, and concurrency/lifecycle outcomes. | Awaiting implementation instruction. |
| 3. Read operations | Migrate availability and reports, including visible timestamp parsing and their distinct empty-result/validation behavior. | Awaiting implementation instruction. |
| 4. Boundary completion | Finish removal of expected-error mappings from the global handler, verify all contracts, and update current architecture documentation and roadmap evidence. | Awaiting implementation instruction. |

Unit 1 is the first design checkpoint. Assess whether its controller and service
can be understood locally before replicating the pattern. A future instruction
may authorize one unit or the remaining sequence; respect its actual scope and
do not ask again for steps already authorized.

During implementation, update README, the domain model, and CONTRIBUTING where
their descriptions of exception flow or API-to-Domain usage become inaccurate.
Review actual direct type usage before changing project references. Keep business
decision documents accurate if affected, and preserve the original assignment.
Record actual validation and remaining work under RF1. Verified requires the
completion criteria below; Done additionally requires a confirmed merge.

The maintainer may refine the plan during implementation. Apply explicit new
instructions within their scope and record material decisions here. Routine
helper names, formatting, and local organization do not require renewed approval.
If an idea changes behavior, dependencies, compatibility, or scope, explain the
consequences and resolve that decision before the affected work. Continue
independent authorized work when possible. This document is maintained with the
code rather than treated as an immutable specification of every private method.

## 8. Later readability work

After the Application/API boundary is evaluated, the agreed next direction is
non-throwing Domain validation, for example `RentalPeriod.TryCreate` with a
Domain-owned typed failure. A throwing `Create` may delegate to the same rule
implementation where invalid arguments indicate misuse. Do not duplicate the
rules or add Domain-to-Application dependencies.

Detailed Domain migration scope and its execution belong to a later checkpoint;
removing every expected exception inside Domain is not a completion criterion
for the first boundary delivery. Other candidates are clearer success-DTO names,
meaningful guard helpers, and repeated persistence-error adapters.

Disabling automatic model validation, introducing an external result library,
adding a shared project, or converting every store operation to a result were
discussed as alternatives, not selected requirements. New endpoints, schema
changes, retries, idempotency, and unrelated formatting are outside this plan.

## 9. Completion criteria and verification

- All four feature areas expose typed expected outcomes from Application, and
  their controllers visibly select success and failure HTTP responses.
- Known request-conversion failures no longer depend on the global exception
  handler. Automatic framework input validation retains its existing contract.
- Expected errors are separated from unexpected failures and cancellation;
  recognized technical failures retain safe responses and useful diagnostics.
- HTTP bodies/statuses/codes/headers, OpenAPI, validation precedence, locks,
  snapshots, and persistence behavior remain compatible.
- Application tests assert outcomes and effects such as no writes, no retries,
  and resource disposal. Existing HTTP/real-PostgreSQL tests continue to protect
  externally observable behavior. Add coverage for identified gaps, including
  missed mappings, mixed failure precedence, or cleanup risks, rather than
  tests that mirror private helpers.
- Current documentation and roadmap evidence match delivered scope. Domain
  validation follow-up remains explicitly separate if it has not been executed.

For preparation, review the diff, verify local Markdown links/anchors, verify
solution item paths and document registration, and build Release. Runtime tests
are unnecessary for documentation-only changes; do not treat this build as
refactor acceptance evidence.

For implementation, use the commands in [CONTRIBUTING](../../CONTRIBUTING.md):
restore as needed, build Release, and run relevant Application/Integration tests
per unit, adding Domain coverage when Domain changes. Before marking the boundary
delivery Verified, run all four implemented suites and review the complete diff,
documentation, and OpenAPI. Record unavailable checks honestly. No database
migration is planned; investigate unexpected model changes rather than adding one
to make a check pass.

## 10. Preparation and validation record

- 2026-09-18: the maintainer accepted the design recommendations and authorized
  branch creation, documentation preparation, and one local documentation commit.
  Implementation permission is explicitly deferred.
- `git fetch origin` confirmed the prerequisite baseline `3f4c2f7`.
  `feature/readability-refactoring` was created with `--no-track`; it has no
  upstream. The first task changes are this plan and its documentation/solution
  references. No production or test source changes are included.
- Preparation verified on 2026-09-18: `dotnet build Confera.slnx --configuration
  Release --no-restore` passed with zero warnings and errors. Checks passed for
  109 local Markdown links/anchors, 38 solution paths, all 17 Markdown document
  registrations, and the plan's `/docs/plans/` solution folder. Diff review and
  whitespace checks passed. Runtime tests were not run for documentation-only
  changes; the build does not compile or validate the illustrative snippets.
- Implementation: not started. No RF1 runtime validation has been performed.
