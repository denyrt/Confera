using System.ComponentModel.DataAnnotations;
using Confera.Application.Rooms;
using Confera.Domain.Rooms;

namespace Confera.Api.Rooms;

public sealed class RoomRequest
{
    [Required]
    public required string Name { get; init; }
    public required int Capacity { get; init; }
    public required decimal HourlyRate { get; init; }
    [Required]
    public required RoomServiceRequest[] Services { get; init; }

    internal RoomCommand ToCommand()
    {
        if (Services.Any(x => x is null))
        {
            throw new RoomOperationException(RoomFailure.InvalidRequest);
        }

        return new RoomCommand(Name, Capacity, HourlyRate, Services.Select(x => new RoomServiceData(x.Name, x.Price)).ToArray());
    }
}

public sealed class RoomServiceRequest
{
    [Required]
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}
