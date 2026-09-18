using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
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

    internal bool TryToCommand([NotNullWhen(true)] out RoomCommand? command)
    {
        command = null;
        if (Services.Any(x => x is null))
        {
            return false;
        }

        command = new RoomCommand(Name, Capacity, HourlyRate, Services.Select(x => new RoomServiceData(x.Name, x.Price)).ToArray());
        return true;
    }
}

public sealed class RoomServiceRequest
{
    [Required]
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}
