using Microsoft.Net.Http.Headers;

namespace Confera.Api.Rooms;

internal static class RoomEntityTags
{
    // Change the format prefix if the selected JSON representation changes incompatibly.
    internal static string Format(Guid version) => $"\"room-v1-{version:N}\"";

    internal static bool TryParse(IHeaderDictionary headers, out Guid[]? expectedVersions)
    {
        expectedVersions = null;
        if (!headers.TryGetValue(HeaderNames.IfMatch, out var values))
        {
            return true;
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values.Select(x => x ?? string.Empty).ToArray(), out var tags) || tags.Count == 0
            || tags.Any(x => x == EntityTagHeaderValue.Any))
        {
            return false;
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

        expectedVersions = versions.ToArray();
        return true;
    }
}
