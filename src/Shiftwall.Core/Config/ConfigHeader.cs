using System.Text.RegularExpressions;

namespace Shiftwall.Core.Config;

/// <summary>
/// The <c>#key=value</c> header lines at the top of a FortiOS backup.
/// </summary>
/// <remarks>
/// Fortinet documents only that the first line "contains information about the firmware version and FortiGate
/// model" and must not be edited (Administration Guide, "CLI basics" - Editing the configuration file). The
/// field layout below matches the examples published on community.fortinet.com, e.g.
/// <c>#config-version=FG6H1F-7.2.8-FW-build1639-240313:opmode=0:vdom=0:user=admin</c>; it is parsed defensively and
/// any unrecognised layout is kept as raw text only.
/// </remarks>
public sealed partial class ConfigHeader
{
    [GeneratedRegex(@"^(?<platform>[A-Za-z0-9_]+)-(?<version>\d+\.\d+\.\d+)-FW-build(?<build>\d+)-(?<stamp>\d{6})(?<rest>(?::[^:=]+=[^:]*)*)$")]
    private static partial Regex ConfigVersionRegex();

    private ConfigHeader(IReadOnlyList<KeyValuePair<string, string>> fields, IReadOnlyList<Statement> lines)
    {
        Fields = fields;
        Lines = lines;
        if (Get("config-version") is { } cv && ConfigVersionRegex().Match(cv.Trim()) is { Success: true } m)
        {
            PlatformCode = m.Groups["platform"].Value;
            Version = m.Groups["version"].Value;
            Build = m.Groups["build"].Value;
            BuildStamp = m.Groups["stamp"].Value;
            foreach (var kv in m.Groups["rest"].Value.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = kv.IndexOf('=');
                var key = kv[..eq];
                var value = kv[(eq + 1)..];
                switch (key)
                {
                    case "opmode": OpMode = value; break;
                    case "vdom": VdomFlag = value; break;
                    case "user": User = value; break;
                }
            }
        }
    }

    /// <summary>All <c>#key=value</c> pairs in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Fields { get; }

    /// <summary>The header statements (comment lines at the top of the file).</summary>
    public IReadOnlyList<Statement> Lines { get; }

    /// <summary>Platform code as written in the header (e.g. <c>FGT60F</c>). Not mapped to a model id: the mapping is not documented.</summary>
    public string? PlatformCode { get; }

    public string? Version { get; }

    public string? Build { get; }

    /// <summary>Six-digit build stamp (yymmdd in published examples).</summary>
    public string? BuildStamp { get; }

    public string? OpMode { get; }

    /// <summary>The <c>vdom=</c> flag of <c>#config-version</c> (<c>1</c> in multi-VDOM backups per published examples).</summary>
    public string? VdomFlag { get; }

    public string? User { get; }

    public bool HasConfigVersion => Get("config-version") is not null;

    public string? Get(string key) => Fields.FirstOrDefault(f => f.Key == key).Value;

    internal static ConfigHeader Parse(IReadOnlyList<Statement> leadingComments)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var s in leadingComments)
        {
            var text = s.Raw.TrimStart(' ', '\t').TrimEnd('\r', '\n');
            if (text.Length < 2 || text[0] != '#') continue;
            var eq = text.IndexOf('=');
            if (eq > 1)
            {
                fields.Add(new(text[1..eq], text[(eq + 1)..]));
            }
        }

        return new ConfigHeader(fields, leadingComments);
    }
}
