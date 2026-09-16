using Confera.Application.Rooms;
using Microsoft.Net.Http.Headers;

namespace Confera.Api.Rooms;

internal static class RoomEntityTags
{
    // Change the format prefix if the selected JSON representation changes incompatibly.
    internal static string Format(Guid version) => $"\"room-v1-{version:N}\"";

    internal static Guid[]? Parse(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(HeaderNames.IfMatch, out var values))
        {
            return null;
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values.Select(x => x ?? string.Empty).ToArray(), out var tags) || tags.Count == 0
            || tags.Any(x => x == EntityTagHeaderValue.Any))
        {
            throw new RoomOperationException(RoomFailure.InvalidRequest);
        }

        var versions = new List<Guid>();
        foreach (var tag in tags)
        {
            if (tag.IsWeak)
            {
                continue;
            }

            var text = tag.Tag.ToString();
            if (text.StartsWith("\"room-v1-", StringComparison.Ordinal) && text.EndsWith('"')
                && Guid.TryParseExact(text.AsSpan(9, text.Length - 10), "N", out var version)
                && text == Format(version))
            {
                versions.Add(version);
            }
        }

        return versions.ToArray();
    }
}
