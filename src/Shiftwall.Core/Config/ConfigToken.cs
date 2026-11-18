using System.Text;

namespace Shiftwall.Core.Config;

/// <summary>
/// A token of a CLI statement, with its exact source text and its unescaped value.
/// </summary>
/// <param name="Start">Offset of <see cref="Raw"/> inside <see cref="Statement.Raw"/>.</param>
/// <param name="Raw">Exact source text, including quotes and escapes.</param>
/// <param name="Value">Value with quotes removed and backslash escapes resolved.</param>
/// <param name="Quote">The quote character if the whole token is quoted (<c>"</c> or <c>'</c>), otherwise null.</param>
public sealed record ConfigToken(int Start, string Raw, string Value, char? Quote)
{
    public int End => Start + Raw.Length;

    public bool IsQuoted => Quote is not null;

    /// <summary>Renders <paramref name="value"/> as a FortiOS CLI token, quoting and escaping when needed.</summary>
    /// <remarks>
    /// FortiOS CLI: spaces and the characters &lt; &gt; ( ) # ' " must be quoted or escaped; a backslash is written \\.
    /// Source: FortiOS Administration Guide, "CLI basics" (Special characters).
    /// </remarks>
    public static string Render(string value, char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(value);
        if (quote is not ('"' or '\''))
        {
            throw new ArgumentOutOfRangeException(nameof(quote), "Quote must be \" or '.");
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append(quote);
        foreach (var c in value)
        {
            if (c is '\\' or '"' or '\'')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        sb.Append(quote);
        return sb.ToString();
    }
}
