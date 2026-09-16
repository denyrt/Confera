using System.Globalization;
using System.Text.RegularExpressions;

namespace Confera.Api.Time;

internal static partial class ApiTimestamp
{
    internal static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || !TimestampPattern().IsMatch(text))
        {
            return false;
        }

        var offsetStart = text.EndsWith('Z') ? text.Length - 1 : text.Length - 6;
        var decimalPoint = text.IndexOf('.');
        if (decimalPoint >= 0)
        {
            var fractionLength = offsetStart - decimalPoint - 1;
            // Check the original digits before parsing can discard or round precision.
            for (var i = decimalPoint + 7; i < offsetStart; i++)
            {
                if (text[i] != '0')
                {
                    return false;
                }
            }

            if (fractionLength > 6)
            {
                text = text.Remove(decimalPoint + 7, fractionLength - 6);
            }
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return false;
        }

        value = value.ToUniversalTime();
        return true;
    }

    // Match the explicit-offset ISO profile accepted by System.Text.Json, including optional seconds.
    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,16})?)?(?:Z|[+-][0-9]{2}:[0-9]{2})\z")]
    private static partial Regex TimestampPattern();
}
