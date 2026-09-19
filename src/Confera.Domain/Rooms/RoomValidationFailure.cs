namespace Confera.Domain.Rooms;

public enum RoomValidationError
{
    InvalidName,
    InvalidCapacity,
    InvalidHourlyRate,
    MissingServices,
    InvalidServiceName,
    InvalidServicePrice,
    DuplicateServiceNames
}

/// <summary>An expected room-input rejection with the existing safe description.</summary>
public sealed class RoomValidationFailure
{
    private readonly InputFailure _inputFailure;

    internal RoomValidationFailure(RoomValidationError kind, InputFailure inputFailure)
    {
        Kind = kind;
        _inputFailure = inputFailure;
    }

    public RoomValidationError Kind { get; }
    public string Description => _inputFailure.Description;

    internal ArgumentException ToArgumentException() => _inputFailure.ToException();
}
