using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Core.Data;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests.Migration;

internal static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Golden", "Migration");

    public static byte[] Bytes(string f) => File.ReadAllBytes(Path.Combine(Dir, f));

    public static string Text(string f) => Encoding.UTF8.GetString(Bytes(f));

    public static readonly ModelDatabase Db = ModelDatabase.LoadEmbedded();

    public static SourceProfile Source(string text) => SourceProfile.From(ConfigInspector.Inspect(Encoding.UTF8.GetBytes(text)));

    public static SourceProfile BranchSource() => SourceProfile.From(ConfigInspector.Inspect(Bytes("branch-source.fgtcfg")));

    public static TargetProfile BranchTarget(string? dbModel = "FG-70G") =>
        TargetProfile.FromDefaultBackup(ConfigInspector.Inspect(Bytes("target-default.fgtcfg")), dbModel is null ? null : Db.FindModel(dbModel));

    public static InterfaceMapping BranchMapping(SourceProfile s) =>
        InterfaceMapping.FromTemplate(MappingTemplate.FromJson(Text("branch-mapping.json")), s.PhysicalInterfaces.Select(p => p.Name));

    /// <summary>A minimal target with the given physical ports (synthetic default backup).</summary>
    public static TargetProfile Target(params string[] ports)
    {
        var sb = new StringBuilder("#config-version=FGTSYN-7.4.13-FW-build2933-261022:opmode=0:vdom=0:user=admin\n#buildno=2933\nconfig system global\n    set hostname \"t\"\nend\nconfig system interface\n");
        foreach (var p in ports) sb.Append("    edit \"").Append(p).Append("\"\n        set type physical\n    next\n");
        sb.Append("end\n");
        return TargetProfile.FromDefaultBackup(ConfigInspector.Inspect(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>A minimal source with the given physical ports and extra configuration text.</summary>
    public static string SourceText(string[] ports, string extra, string version = "7.4.13")
    {
        var sb = new StringBuilder($"#config-version=FGTSRC-{version}-FW-build2933-261022:opmode=0:vdom=0:user=admin\n#buildno=2933\nconfig system global\n    set hostname \"s\"\nend\nconfig system interface\n");
        foreach (var p in ports) sb.Append("    edit \"").Append(p).Append("\"\n        set type physical\n    next\n");
        sb.Append("end\n").Append(extra);
        return sb.ToString();
    }
}
