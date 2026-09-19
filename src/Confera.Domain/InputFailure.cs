namespace Confera.Domain;

// Shared guard data; exceptions are constructed only by throwing compatibility APIs.
internal sealed record InputFailure(InputFailureKind Kind, string Message, string ParameterName)
{
    // Keep the safe detail text historically exposed by room validation, including its parameter.
    public string Description => $"{Message} (Parameter '{ParameterName}')";

    public ArgumentException ToException() => Kind switch
    {
        InputFailureKind.Null => new ArgumentNullException(ParameterName),
        InputFailureKind.OutOfRange => new ArgumentOutOfRangeException(ParameterName, Message),
        _ => new ArgumentException(Message, ParameterName)
    };

    public static InputFailure Null(string parameterName) =>
        new(InputFailureKind.Null, "Value cannot be null.", parameterName);
}

internal enum InputFailureKind
{
    Invalid,
    Null,
    OutOfRange
}
