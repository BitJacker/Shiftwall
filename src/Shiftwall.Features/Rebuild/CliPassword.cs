using System.Security.Cryptography;

namespace Shiftwall.Features.Rebuild;

/// <summary>
/// Passwords that can be typed, pasted or uploaded in a FortiOS script without quoting problems.
/// </summary>
/// <remarks>
/// FortiOS Administration Guide 8.0.2, "CLI basics" (Special characters): &lt; &gt; ( ) # ' " cannot be used in most
/// commands without quoting or escaping, a space ends the value, a backslash is an escape character, and ? and Tab
/// cannot be pasted into the CLI Console or some SSH clients. None of them is ever generated or accepted here.
/// FortiOS 8.0.2 Release Notes, "Password policy enforcement": at least 12 characters with an upper-case letter,
/// a lower-case letter, a number and a special character. Generated passwords are 20 characters with all four classes.
/// </remarks>
public static class CliPassword
{
    public const int GeneratedLength = 20;

    /// <summary>The minimum Shiftwall accepts for a password typed by the user (FortiOS 7.6.5+ admin policy: 12).</summary>
    public const int MinimumLength = 12;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>Special characters that need no quoting or escaping and can be pasted.</summary>
    public const string Symbols = "!$%&*+-=@^_~";

    /// <summary>Characters the FortiOS CLI documentation lists as special (plus space, tab and backslash).</summary>
    public const string Forbidden = "?<>()#'\"\\ \t";

    /// <summary>A new random password, different every call. Never a fixed value.</summary>
    public static string Generate()
    {
        const string all = Upper + Lower + Digits + Symbols;
        var chars = new char[GeneratedLength];
        chars[0] = RandomNumberGenerator.GetItems<char>(Upper, 1)[0];
        chars[1] = RandomNumberGenerator.GetItems<char>(Lower, 1)[0];
        chars[2] = RandomNumberGenerator.GetItems<char>(Digits, 1)[0];
        chars[3] = RandomNumberGenerator.GetItems<char>(Symbols, 1)[0];
        RandomNumberGenerator.GetItems<char>(all, GeneratedLength - 4).CopyTo(chars.AsSpan(4));
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    public enum Problem
    {
        TooShort,
        MissingUpper,
        MissingLower,
        MissingDigit,
        MissingSymbol,
        ForbiddenCharacter,
        NotAscii,
    }

    /// <summary>Checks a password typed by the user. Returns an empty list when it is acceptable.</summary>
    public static IReadOnlyList<Problem> Check(string? password)
    {
        var p = password ?? "";
        var problems = new List<Problem>();
        if (p.Length < MinimumLength) problems.Add(Problem.TooShort);
        if (!p.Any(char.IsAsciiLetterUpper)) problems.Add(Problem.MissingUpper);
        if (!p.Any(char.IsAsciiLetterLower)) problems.Add(Problem.MissingLower);
        if (!p.Any(char.IsAsciiDigit)) problems.Add(Problem.MissingDigit);
        if (!p.Any(c => Symbols.Contains(c, StringComparison.Ordinal))) problems.Add(Problem.MissingSymbol);
        if (p.Any(c => Forbidden.Contains(c, StringComparison.Ordinal) || char.IsControl(c))) problems.Add(Problem.ForbiddenCharacter);
        if (p.Any(c => c > 126)) problems.Add(Problem.NotAscii);
        return problems;
    }
}
