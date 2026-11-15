using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Shiftwall.Core.Data;

/// <summary>A FortiOS version <c>major.minor.patch</c>, ordered numerically (7.0.10 &gt; 7.0.9).</summary>
public readonly record struct FortiOsVersion(int Major, int Minor, int Patch) : IComparable<FortiOsVersion>
{
    /// <summary>The release branch, e.g. <c>7.4</c>.</summary>
    public string Branch => $"{Major}.{Minor}";

    public int CompareTo(FortiOsVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    public static bool operator <(FortiOsVersion a, FortiOsVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(FortiOsVersion a, FortiOsVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(FortiOsVersion a, FortiOsVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(FortiOsVersion a, FortiOsVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static FortiOsVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a FortiOS version (expected major.minor.patch).");

    public static bool TryParse([NotNullWhen(true)] string? text, out FortiOsVersion version)
    {
        version = default;
        var parts = text?.Split('.');
        if (parts is not { Length: 3 })
        {
            return false;
        }

        if (!TryPart(parts[0], out var major) || !TryPart(parts[1], out var minor) || !TryPart(parts[2], out var patch))
        {
            return false;
        }

        version = new FortiOsVersion(major, minor, patch);
        return true;

        static bool TryPart(string s, out int value) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && s.Length <= 3;
    }
}
