using System.Text;

namespace Confera.Domain;

/// <summary>
/// Produces the canonical name key matching the PostgreSQL 18
/// pg_c_utf8 simple-uppercase uniqueness semantics.
/// </summary>
public static class NameIdentity
{
    public const string TrimCharacters =
        "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";

    public static string Trim(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.AsSpan()
            .Trim(TrimCharacters.AsSpan())
            .ToString();
    }

    public static string Key(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.AsSpan().Trim(TrimCharacters.AsSpan());
        var key = new StringBuilder(capacity: trimmed.Length);
        Span<char> buffer = stackalloc char[2];

        foreach (var scalar in trimmed.EnumerateRunes())
        {
            var written = PostgresUpper(scalar).EncodeToUtf16(buffer);
            key.Append(buffer[..written]);
        }

        return key.ToString();
    }

    private static Rune PostgresUpper(Rune scalar)
    {
        // PostgreSQL 18 Unicode simple mapping includes Unicode 16 pairs that
        // older ICU/NLS runtimes lack. Invariant .NET also leaves dotless I alone.
        var mapped = scalar.Value switch
        {
            0x0131 => 0x0049,
            0x019B => 0xA7DC,
            0x0264 => 0xA7CB,
            0x1C8A => 0x1C89,
            0xA7CD => 0xA7CC,
            0xA7DB => 0xA7DA,
            >= 0x10D70 and <= 0x10D85 => scalar.Value - 0x20,
            _ => Rune.ToUpperInvariant(scalar).Value
        };

        return new Rune(mapped);
    }
}
