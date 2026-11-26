using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>How the address of an interface is set on the target.</summary>
public enum AddressingMode
{
    /// <summary>As on the source (static, DHCP or PPPoE). A static source is written with "set mode static" because the target may default to DHCP.</summary>
    KeepSource,
    Static,
    Dhcp,
    Pppoe,
}

/// <summary>Where a source interface goes on the target, and how it is addressed.</summary>
/// <param name="Address">For <see cref="AddressingMode.Static"/>: "a.b.c.d/nn" or "a.b.c.d m.m.m.m".</param>
/// <param name="NameServers">For <see cref="AddressingMode.Static"/>: DNS servers written in system dns.</param>
/// <param name="Gateway">For <see cref="AddressingMode.Static"/>: default gateway, written as a static default route.</param>
public sealed record InterfacePlan(
    string Source,
    string? Target,
    AddressingMode Mode = AddressingMode.KeepSource,
    string? Address = null,
    string? Gateway = null,
    IReadOnlyList<string>? NameServers = null);

public enum SecretCategory
{
    Admin,
    LocalUser,
    VpnPsk,

    /// <summary>LDAP bind passwords, RADIUS secrets: they must match the external server.</summary>
    Server,

    /// <summary>PPPoE passwords: given by the provider.</summary>
    Pppoe,
    Other,
}

/// <summary>
/// The passwords written in place of the ENC values, which are never copied (ENC values are encrypted with a key
/// of the source device and are never decrypted by Shiftwall).
/// </summary>
public sealed class SecretPlan
{
    /// <summary>Generate a new random password per category for this migration (never a fixed value).</summary>
    public bool Generate { get; init; }

    /// <summary>Passwords typed by the user, per category. They override the generated ones.</summary>
    public IReadOnlyDictionary<SecretCategory, string> Provided { get; init; } = new Dictionary<SecretCategory, string>();

    /// <summary>Only these can be generated: the others must match something outside the FortiGate.</summary>
    public static bool CanGenerate(SecretCategory c) => c is SecretCategory.Admin or SecretCategory.LocalUser or SecretCategory.VpnPsk;
}

/// <summary>What the rebuild knows about the target FortiGate.</summary>
/// <param name="Interfaces">Interfaces that exist on the target out of the box: physical ports and logical ones (hard or software switches) from a factory-default backup.</param>
/// <param name="LowMemory">2 GB RAM model (FortiOS 8.0.2 Release Notes, Special notices): no SSL VPN web mode, no proxy features.</param>
public sealed record RebuildTarget(
    string? ModelId,
    string Version,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<VirtualSwitch> Switches,
    ConfigDocument? DefaultBackup,
    bool LowMemory);

public sealed record RebuildOptions(
    string CurrentAdmin = "admin",
    string? ManagementInterface = null,
    bool ConvertSslVpn = true,
    string DialupTunnelName = "dialup_vpn");

public sealed record RebuildRequest(
    ConfigDocument Source,
    string SourceVersion,
    RebuildTarget Target,
    IReadOnlyList<InterfacePlan> Interfaces,
    SecretPlan Secrets,
    RebuildOptions? Options = null);

/// <summary>Something Shiftwall did not put in the script, with the reason and what to do.</summary>
/// <param name="Original">Source text of the object or line, with ENC values masked.</param>
public sealed record ManualItem(string Path, string Object, string Code, IReadOnlyList<string> Args, string Original, string? RuleId, IReadOnlyList<RuleSource> Sources)
{
    public string Format(System.Globalization.CultureInfo? culture = null) => MigrationText.Format(Code, Args, null, culture);
}

/// <summary>Source → target count for one object type ("policy: 48 → 48").</summary>
public sealed record ObjectCount(string Path, int Source, int Rebuilt, int Converted, int Manual);

public sealed record AppliedRule(string RuleId, string Path, string Object, string Param);

/// <summary>A password written in the script. Never logged, never in reports: only in the encrypted summary.</summary>
public sealed record SecretAssignment(SecretCategory Category, string Path, string Object, string Param, string Password, bool Generated);

public sealed record RebuildResult(
    IReadOnlyList<ScriptSection> Sections,
    IReadOnlyList<ManualItem> Manual,
    IReadOnlyList<ObjectCount> Counts,
    IReadOnlyList<AppliedRule> Applied,
    IReadOnlyList<SecretAssignment> Secrets,
    IReadOnlyList<MigrationFinding> Findings,
    IReadOnlyList<LintIssue> Lint,
    CliSchema TargetSchema)
{
    public bool IsBlocked => Findings.Any(f => f.Severity == FindingSeverity.Error);

    public bool LintPassed => Lint.All(l => l.Severity != LintSeverity.Error);

    public string FullScript(bool maskSecrets = false) => CliRenderer.Render(Sections.Where(s => !s.IsEmpty).OrderBy(s => s.Order).SelectMany(s => s.Blocks), maskSecrets);

    public IReadOnlyList<ScriptFile> GuiFiles(string title) => ScriptPackager.ForGui(Sections, title);

    public IReadOnlyList<ScriptFile> TerminalFiles() => ScriptPackager.ForTerminal(Sections);
}
