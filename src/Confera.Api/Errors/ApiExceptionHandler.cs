using Confera.Application.Availability;
using Confera.Application.Bookings;
using Confera.Application.Rooms;
using Confera.Domain.Bookings;
using Confera.Domain.Rooms;
using Microsoft.AspNetCore.Diagnostics;

namespace Confera.Api.Errors;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var (status, code, detail) = exception switch
        {
            AvailabilityOperationException { Failure: AvailabilityFailure.InvalidRequest } =>
                (400, "invalid_request", "Supply explicit-offset timestamps, a positive capacity and valid page/pageSize values."),
            AvailabilityOperationException { Failure: AvailabilityFailure.PersistenceUnavailable } =>
                (503, "availability_persistence_unavailable", "Availability search is temporarily unavailable."),
            RoomValidationException error => (400, "invalid_room_data", error.Message),
            RoomOperationException { Failure: RoomFailure.InvalidRequest } =>
                (400, "invalid_request", "Supply a nonempty room ID and valid explicit entity tags in If-Match; wildcard * is not supported."),
            RoomOperationException { Failure: RoomFailure.NotFound } =>
                (404, "room_not_found", "The room was not found."),
            RoomOperationException { Failure: RoomFailure.NameConflict } =>
                (409, "room_name_conflict", "An active room already uses this name."),
            RoomOperationException { Failure: RoomFailure.HasUnfinishedBookings } =>
                (409, "room_has_unfinished_bookings", "The room has ongoing or future bookings; capacity reduction and deletion are unavailable."),
            RoomOperationException { Failure: RoomFailure.VersionMismatch } =>
                (412, "room_version_mismatch", "The room has changed. Read it again and review your changes before submitting the new ETag in If-Match."),
            RoomOperationException { Failure: RoomFailure.PreconditionRequired } =>
                (428, "room_precondition_required", "Read the room and supply its ETag in the If-Match header."),
            RoomOperationException { Failure: RoomFailure.PersistenceUnavailable } =>
                (503, "room_persistence_unavailable", "The room operation could not be confirmed. A failed response does not prove that no change was saved."),
            BookingValidationException { Error: BookingValidationError.InvalidPeriod } error =>
                (400, "invalid_booking_period", error.Message),
            BookingValidationException { Error: BookingValidationError.InvalidServiceSelection } error =>
                (400, "invalid_service_selection", error.Message),
            BookingValidationException { Error: BookingValidationError.MissingTariffCoverage } error =>
                (400, "tariff_coverage_missing", error.Message),
            BookingOperationException { Failure: BookingFailure.InvalidRequest } =>
                (400, "invalid_request", "Supply a nonempty room ID."),
            BookingOperationException { Failure: BookingFailure.RoomNotFound } =>
                (404, "room_not_found", "The room was not found."),
            BookingOperationException { Failure: BookingFailure.RoomUnavailable } =>
                (409, "room_unavailable", "The room is already booked for part of this period."),
            BookingOperationException { Failure: BookingFailure.PersistenceUnavailable } =>
                (503, "booking_persistence_unavailable", "The booking result could not be confirmed. A failed response does not prove that no booking was saved."),
            _ => (500, "internal_error", "The request could not be completed.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Request {TraceId} failed with {Code}.", context.TraceIdentifier, code);
        }

        if (status == 428)
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        await Results.Problem(ApiProblems.Create(context, status, code, detail)).ExecuteAsync(context);
        return true;
    }
}
