using System.Globalization;
using System.Net;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>
/// "Rebuild" migration: the source configuration is read object by object and written again as a clean CLI script
/// in the syntax of the target version, to be run on a target FortiGate with its factory configuration.
/// Every parameter goes through the translation rules (data/rules) and is checked on the CLI schema of the target
/// version (data/schemas); what does not pass is never exported and is listed as manual work with the reason.
/// ENC values are never copied nor decrypted: they are replaced by the passwords of the <see cref="SecretPlan"/>.
/// </summary>
public sealed partial class RebuildEngine
{
    private enum Ref
    {
        Interface,
        Address,
        AddressOrVip,
        Service,
        ServiceCategory,
        Schedule,
        Vip,
        Pool,
        User,
        Group,
        UserOrServer,
        Ldap,
        Radius,
        Phase1,
        AccProfile,
    }

    /// <summary>Parameters that name other objects. Unresolved names keep the object out of the script.</summary>
    private static readonly Dictionary<(string Path, string Param), Ref> References = new()
    {
        [("system interface", "interface")] = Ref.Interface,
        [("system interface", "member")] = Ref.Interface,
        [("system zone", "interface")] = Ref.Interface,
        [("system dhcp server", "interface")] = Ref.Interface,
        [("firewall address", "associated-interface")] = Ref.Interface,
        [("firewall addrgrp", "member")] = Ref.Address,
        [("firewall service custom", "category")] = Ref.ServiceCategory,
        [("firewall service group", "member")] = Ref.Service,
        [("firewall schedule group", "member")] = Ref.Schedule,
        [("firewall vip", "extintf")] = Ref.Interface,
        [("firewall vipgrp", "interface")] = Ref.Interface,
        [("firewall vipgrp", "member")] = Ref.Vip,
        [("user local", "ldap-server")] = Ref.Ldap,
        [("user local", "radius-server")] = Ref.Radius,
        [("user group", "member")] = Ref.UserOrServer,
        [("vpn ipsec phase1-interface", "interface")] = Ref.Interface,
        [("vpn ipsec phase1-interface", "authusrgrp")] = Ref.Group,
        [("vpn ipsec phase1-interface", "usrgrp")] = Ref.Group,
        [("vpn ipsec phase2-interface", "phase1name")] = Ref.Phase1,
        [("vpn ipsec phase2-interface", "src-name")] = Ref.Address,
        [("vpn ipsec phase2-interface", "dst-name")] = Ref.Address,
        [("router static", "device")] = Ref.Interface,
        [("router static", "dstaddr")] = Ref.Address,
        [("firewall policy", "srcintf")] = Ref.Interface,
        [("firewall policy", "dstintf")] = Ref.Interface,
        [("firewall policy", "srcaddr")] = Ref.AddressOrVip,
        [("firewall policy", "dstaddr")] = Ref.AddressOrVip,
        [("firewall policy", "service")] = Ref.Service,
        [("firewall policy", "schedule")] = Ref.Schedule,
        [("firewall policy", "groups")] = Ref.Group,
        [("firewall policy", "users")] = Ref.User,
        [("firewall policy", "poolname")] = Ref.Pool,
        [("system admin", "accprofile")] = Ref.AccProfile,
    };

    /// <summary>Policy parameters that point to security profiles. Profiles are not rebuilt (see rule E002).</summary>
    private static readonly HashSet<string> ProfileParams =
    [
        "av-profile", "webfilter-profile", "application-list", "ips-sensor", "spamfilter-profile", "emailfilter-profile",
        "dlp-sensor", "dlp-profile", "icap-profile", "voip-profile", "waf-profile", "dnsfilter-profile", "profile-group",
        "profile-protocol-options", "ssl-ssh-profile", "webcache", "casi-profile",
    ];

    /// <summary>Tables of profiles in a factory-default backup, to keep references to profiles that exist there.</summary>
    private static readonly Dictionary<string, string> ProfileTables = new()
    {
        ["av-profile"] = "antivirus profile",
        ["webfilter-profile"] = "webfilter profile",
        ["application-list"] = "application list",
        ["ips-sensor"] = "ips sensor",
        ["dnsfilter-profile"] = "dnsfilter profile",
        ["profile-protocol-options"] = "firewall profile-protocol-options",
        ["ssl-ssh-profile"] = "firewall ssl-ssh-profile",
    };

    /// <summary>Parameter names that hold secrets: masked in every report even when not ENC.</summary>
    internal static readonly HashSet<string> SecretParams = new(StringComparer.Ordinal)
    {
        "password", "passwd", "psksecret", "secret", "secondary-secret", "tertiary-secret", "ppk-secret", "key", "auth-pwd",
        "priv-pwd", "md5-key", "passphrase", "server-key", "private-key",
    };

    private readonly RebuildRequest _req;
    private readonly RebuildOptions _opt;
    private readonly CliSchema _target;
    private readonly RuleSet _rules;
    private readonly List<ManualItem> _manual = [];
    private readonly List<AppliedRule> _applied = [];
    private readonly List<SecretAssignment> _secrets = [];
    private readonly List<MigrationFinding> _findings = [];
    private readonly Dictionary<string, (int Source, int Rebuilt, int Converted, int Manual)> _counts = [];
    private readonly Dictionary<Ref, HashSet<string>> _known = [];
    private readonly Dictionary<string, string> _ifMap = new(StringComparer.Ordinal);
    private readonly Dictionary<SecretCategory, string> _generated = [];
    private readonly Dictionary<string, ScriptSection> _sections = [];
    private readonly HashSet<string> _readdressed = new(StringComparer.Ordinal);
    private readonly HashSet<(string Path, string Key)> _emittedKeys = [];
    private readonly HashSet<string> _noDefaultRoute = new(StringComparer.Ordinal);

    private RebuildEngine(RebuildRequest req, CliSchema target, RuleSet rules)
    {
        _req = req;
        _opt = req.Options ?? new RebuildOptions();
        _target = target;
        _rules = rules;
        foreach (var r in Enum.GetValues<Ref>()) _known[r] = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Major.minor of a version string ("5.2.15" → "5.2").</summary>
    public static string MajorMinor(string? version)
    {
        // Backups of FortiOS 5.x write "5.02" for 5.2; later versions write "6.4.15".
        if (string.IsNullOrEmpty(version)) return "";
        var parts = version.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var major) || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var minor)) return version;
        return major.ToString(CultureInfo.InvariantCulture) + "." + minor.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>True when Shiftwall has a schema and rules for this pair of versions.</summary>
    public static bool Supports(string? sourceVersion, string? targetVersion) =>
        RuleSet.Exists(MajorMinor(sourceVersion), MajorMinor(targetVersion)) && CliSchema.Bundled.Contains(MajorMinor(targetVersion));

    public static RebuildResult Run(RebuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var from = MajorMinor(request.SourceVersion);
        var to = MajorMinor(request.Target.Version);
        var target = CliSchema.Load(to);
        var rules = RuleSet.Load(from, to);
        var e = new RebuildEngine(request, target, rules);
        e.Build();
        e.VerifyReferences();
        var sections = e._sections.Values.OrderBy(s => s.Order).ToList();
        var script = CliRenderer.Render(sections.Where(s => !s.IsEmpty).SelectMany(s => s.Blocks));
        var lint = CliLint.Check(script, target, allowComments: false);
        var counts = e._counts.Select(kv => new ObjectCount(kv.Key, kv.Value.Source, kv.Value.Rebuilt, kv.Value.Converted, kv.Value.Manual)).ToList();
        return new RebuildResult(sections, e._manual, counts, e._applied, e._secrets, e._findings, lint, target);
    }

    // ------------------------------------------------------------------ build

    private void Build()
    {
        var doc = _req.Source;
        if (doc.IsMultiVdom || doc.Block("vdom") is not null || doc.Block("global") is not null)
        {
            _findings.Add(MigrationFinding.Error(RebuildCodes.MultiVdomNotSupported));
            return;
        }

        if (doc.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            _findings.Add(MigrationFinding.Error(RebuildCodes.SourceHasErrors));
            return;
        }

        // A missing "end" balanced by a later one nests whole sections inside others: never rebuilt silently.
        foreach (var nested in doc.Children.OfType<ConfigContainer>().SelectMany(c => c.Descendants()).OfType<ConfigBlock>().Where(b => b.Path.Contains(' ', StringComparison.Ordinal)))
        {
            _findings.Add(new MigrationFinding(RebuildCodes.NestedBlock, FindingSeverity.Error, [nested.Path], nested.Statement.Line));
        }

        foreach (var (category, password) in _req.Secrets.Provided)
        {
            var problems = CliPassword.Check(password);
            if (problems.Contains(CliPassword.Problem.ForbiddenCharacter) || problems.Contains(CliPassword.Problem.NotAscii))
            {
                _findings.Add(MigrationFinding.Error(RebuildCodes.ProvidedPasswordInvalid, CategoryName(category)));
            }
        }

        if (_findings.Count > 0) return;

        foreach (var t in _req.Target.Interfaces) _known[Ref.Interface].Add(t);
        _known[Ref.Interface].Add("any");
        foreach (var p in AccProfilesOnTarget()) _known[Ref.AccProfile].Add(p);

        Section(10, "switch");
        Section(20, "interfaces");
        Section(25, "zones");
        Section(28, "dns");
        Section(30, "addresses");
        Section(40, "services");
        Section(50, "schedules");
        Section(60, "vips");
        Section(70, "users");
        Section(80, "ipsec");
        Section(90, "routes");
        Section(100, "policies");
        Section(110, "admins");
        Section(120, "dhcp");
        Section(130, "management");
        Section(140, "management-dhcp");

        var ssl = SslVpnInfo.Read(doc);
        PlanInterfaces(doc, ssl);
        RebuildSwitchPorts();
        RebuildInterfaces(doc, ssl);
        Simple(doc, "system zone", "zones", Ref.Interface, register: Ref.Interface);
        RebuildGlobal(doc);
        Simple(doc, "system dns", "dns");
        Simple(doc, "firewall address", "addresses", register: Ref.Address, skip: n => ssl.Present && _opt.ConvertSslVpn && ssl.IsSslObject("firewall address", n));
        Ordered(doc, "firewall addrgrp", "addresses", Ref.Address, "member");
        Simple(doc, "firewall service category", "services", register: Ref.ServiceCategory);
        Simple(doc, "firewall service custom", "services", register: Ref.Service);
        Ordered(doc, "firewall service group", "services", Ref.Service, "member");
        Simple(doc, "firewall schedule onetime", "schedules", register: Ref.Schedule);
        Simple(doc, "firewall schedule recurring", "schedules", register: Ref.Schedule);
        Simple(doc, "firewall schedule group", "schedules", register: Ref.Schedule);
        Simple(doc, "firewall vip", "vips", register: Ref.Vip);
        Ordered(doc, "firewall vipgrp", "vips", Ref.Vip, "member");
        Simple(doc, "firewall ippool", "vips", register: Ref.Pool);
        Simple(doc, "user ldap", "users", register: Ref.Ldap);
        Simple(doc, "user radius", "users", register: Ref.Radius);
        Simple(doc, "user local", "users", register: Ref.User);
        Simple(doc, "user group", "users", register: Ref.Group);
        RebuildIpsec(doc);
        if (ssl.Present) ConvertSslVpn(doc, ssl);
        RebuildRoutes(doc);
        RebuildPolicies(doc, ssl);
        CheckPolicyRoutes();
        RebuildAdmins(doc);
        RebuildDhcp(doc);
        ReportOtherSections(doc, ssl);
        AddFindings(ssl);
    }

    /// <summary>Only the general settings that do not depend on the hardware are taken from "system global".</summary>
    private static readonly HashSet<string> GlobalParams = new(StringComparer.Ordinal) { "hostname", "timezone", "admintimeout", "admin-sport", "admin-ssh-port", "alias" };

    private void RebuildGlobal(ConfigDocument doc)
    {
        const string path = "system global";
        var node = _target.Find(path);
        if (node is null) return;
        foreach (var block in BlocksOf(doc, path))
        {
            var drop = block.Fields.Select(f => f.Name).Where(n => !GlobalParams.Contains(n)).ToHashSet(StringComparer.Ordinal);
            var t = Translate(path, [], block, node, null, path, null, drop);
            Count(path, source: 1, rebuilt: 1);
            Emit("dns", path, t, path, block);
            if (drop.Count > 0) _manual.Add(Item(path, path, RebuildCodes.GlobalPartial, [string.Join(", ", drop)], Mask(block.ToText()), null));
        }
    }

    private ScriptSection Section(int order, string key)
    {
        if (!_sections.TryGetValue(key, out var s))
        {
            s = new ScriptSection(order, key, []);
            _sections[key] = s;
        }

        return s;
    }

    private CliBlock BlockIn(string section, string path)
    {
        var s = _sections[section];
        var existing = s.Blocks.LastOrDefault();
        if (existing is not null && existing.Path == path) return existing;
        var b = new CliBlock(path, _target.Find(path)?.IsTable ?? true);
        s.Blocks.Add(b);
        return b;
    }

    private void Count(string path, int source = 0, int rebuilt = 0, int converted = 0, int manual = 0)
    {
        var c = _counts.GetValueOrDefault(path);
        _counts[path] = (c.Source + source, c.Rebuilt + rebuilt, c.Converted + converted, c.Manual + manual);
    }

    private static IEnumerable<ConfigBlock> BlocksOf(ConfigDocument doc, string path) => doc.Blocks.Where(b => b.Path == path);

    /// <summary>Tables whose entries can be the target of each kind of reference.</summary>
    private static readonly Dictionary<Ref, string[]> Definitions = new()
    {
        [Ref.Interface] = ["system interface", "system zone", "vpn ipsec phase1-interface"],
        [Ref.Address] = ["firewall address", "firewall addrgrp"],
        [Ref.AddressOrVip] = ["firewall address", "firewall addrgrp", "firewall vip", "firewall vipgrp"],
        [Ref.Service] = ["firewall service custom", "firewall service group"],
        [Ref.ServiceCategory] = ["firewall service category"],
        [Ref.Schedule] = ["firewall schedule onetime", "firewall schedule recurring", "firewall schedule group"],
        [Ref.Vip] = ["firewall vip"],
        [Ref.Pool] = ["firewall ippool"],
        [Ref.User] = ["user local"],
        [Ref.Group] = ["user group"],
        [Ref.UserOrServer] = ["user local", "user ldap", "user radius"],
        [Ref.Ldap] = ["user ldap"],
        [Ref.Radius] = ["user radius"],
        [Ref.Phase1] = ["vpn ipsec phase1-interface"],
        [Ref.AccProfile] = ["system accprofile"],
    };

    /// <summary>
    /// Final check before export: every name used by the script is defined in the script itself or exists on the
    /// target out of the box. A broken reference is a bug and blocks the export.
    /// </summary>
    private void VerifyReferences()
    {
        var blocks = _sections.Values.SelectMany(s => s.Blocks).ToList();
        var defined = blocks.GroupBy(b => b.Path).ToDictionary(g => g.Key, g => g.SelectMany(b => b.Entries).Select(x => x.Key).OfType<string>().ToHashSet(StringComparer.Ordinal));
        bool Exists(Ref kind, string name) =>
            Definitions[kind].Any(p => defined.TryGetValue(p, out var set) && set.Contains(name))
            || (kind == Ref.Interface && (_req.Target.Interfaces.Contains(name, StringComparer.Ordinal) || name == "any"))
            || (kind == Ref.AccProfile && AccProfilesOnTarget().Contains(name));
        foreach (var b in blocks)
        {
            foreach (var entry in b.Entries)
            {
                foreach (var set in entry.Sets)
                {
                    if (!References.TryGetValue((b.Path, set.Param), out var kind)) continue;
                    foreach (var v in set.Values.Where(v => !Exists(kind, v)))
                    {
                        _findings.Add(MigrationFinding.Error(RebuildCodes.BrokenReference, b.Path, entry.Key ?? "", set.Param, v));
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ generic translation

    /// <summary>The outcome of translating one object.</summary>
    private sealed class Translation
    {
        public CliEntry Entry { get; init; } = null!;

        /// <summary>Set when the object cannot be exported at all (with the reason code and arguments).</summary>
        public (string Code, string[] Args)? Fatal { get; set; }

        public List<ManualItem> FieldItems { get; } = [];
    }

    private delegate IReadOnlyList<string>? ValueHook(string param, IReadOnlyList<string> values, Translation t);

    /// <summary>Translates the statements of <paramref name="src"/> (an edit entry or a non-table block) to <paramref name="node"/>.</summary>
    private Translation Translate(string path, IReadOnlyList<string> sub, ConfigContainer src, SchemaNode node, string? key, string objectName, ValueHook? hook = null, ISet<string>? drop = null)
    {
        var t = new Translation { Entry = new CliEntry(key) };
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in src.Children)
        {
            switch (child)
            {
                case FieldNode f when f.Verb == "set":
                    present.Add(f.Name);
                    if (drop?.Contains(f.Name) == true) break;
                    TranslateField(path, sub, node, f, objectName, hook, t);
                    break;
                case ConfigBlock b:
                    TranslateTable(path, sub, node, b, objectName, hook, t);
                    break;
            }

            if (t.Fatal is not null) return t;
        }

        // Defaults that changed between the versions: written explicitly when the source relied on the old default.
        var insertAt = t.Entry.Sets.FindLastIndex(s => s.Param is "type" or "interface" or "vdom" or "phase1name") + 1;
        foreach (var rule in _rules.Defaults(path, sub))
        {
            if (rule.Param is null || present.Contains(rule.Param) || rule.Value is null) continue;
            if (!node.Params.TryGetValue(rule.Param, out var p)) continue;
            var values = rule.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (CliSchema.Check(p, values) is not null) continue;
            if (!WhenHolds(rule, t.Entry, node)) continue;
            t.Entry.Sets.Insert(insertAt++, new CliSet(rule.Param, values, IsQuoted(p)));
            _applied.Add(new AppliedRule(rule.Id, path, objectName, rule.Param));
        }

        return t;
    }

    /// <summary>A default-change rule with conditions applies only when the object has those values (or defaults).</summary>
    private static bool WhenHolds(TranslationRule rule, CliEntry entry, SchemaNode node)
    {
        foreach (var (param, allowed) in rule.When)
        {
            var value = entry.Sets.FirstOrDefault(s => s.Param == param)?.Values.FirstOrDefault()
                ?? node.Params.GetValueOrDefault(param)?.Default;
            if (value is null || !allowed.Contains(value, StringComparer.Ordinal)) return false;
        }

        return true;
    }

    private void TranslateField(string path, IReadOnlyList<string> sub, SchemaNode node, FieldNode f, string objectName, ValueHook? hook, Translation t)
    {
        var param = f.Name;
        IReadOnlyList<string> values = f.Values;
        var rule = _rules.ForParam(path, sub, param, values);
        if (rule is not null && rule.Kind != "not-a-difference")
        {
            switch (rule.Kind)
            {
                case "skip":
                    _applied.Add(new AppliedRule(rule.Id, path, objectName, param));
                    return;
                case "remove-no-equivalent" or "remove-with-equivalent":
                    _applied.Add(new AppliedRule(rule.Id, path, objectName, param));
                    t.FieldItems.Add(Item(path, objectName, rule.Kind == "remove-no-equivalent" ? RebuildCodes.RemovedNoEquivalent : RebuildCodes.RemovedWithEquivalent, [param, rule.Note], Mask(f.Statement.Raw), rule));
                    return;
                case "rename":
                    _applied.Add(new AppliedRule(rule.Id, path, objectName, param));
                    param = rule.To!;
                    break;
                case "value-format":
                    if (values.Count == 1 && rule.Value is not null)
                    {
                        _applied.Add(new AppliedRule(rule.Id, path, objectName, param));
                        values = [values[0], rule.Value];
                    }

                    break;
                case "value-map":
                    if (values.Any(rule.Map.ContainsKey))
                    {
                        _applied.Add(new AppliedRule(rule.Id, path, objectName, param));
                        values = [.. values.Select(v => rule.Map.GetValueOrDefault(v, v))];
                    }

                    break;
            }
        }

        if (hook is not null)
        {
            var hooked = hook(param, values, t);
            if (t.Fatal is not null) return;
            if (hooked is null) return; // handled (or dropped) by the hook
            values = hooked;
        }

        if (values.Count > 0 && values[0] == "ENC")
        {
            // A secret the hook did not handle: never copied.
            t.FieldItems.Add(Item(path, objectName, RebuildCodes.SecretNotMigrated, [param], Mask(f.Statement.Raw), null));
            return;
        }

        if (References.TryGetValue((path, param), out var kind) && sub.Count == 0)
        {
            var mapped = Resolve(kind, values, out var missing);
            if (missing is not null)
            {
                t.Fatal = (RebuildCodes.UnresolvedReference, [param, missing]);
                return;
            }

            values = mapped;
        }

        if (!node.Params.TryGetValue(param, out var p))
        {
            t.FieldItems.Add(Item(path, objectName, RebuildCodes.ParamNotInTarget, [param, _target.Version], Mask(f.Statement.Raw), null));
            return;
        }

        if (CliSchema.Check(p, values) is { } problem)
        {
            t.FieldItems.Add(Item(path, objectName, RebuildCodes.InvalidValue, [param, problem.ToString(), _target.Version], Mask(f.Statement.Raw), null));
            return;
        }

        if (values.Any(v => v.Contains('?', StringComparison.Ordinal) || v.Contains('\t', StringComparison.Ordinal) || v.Contains('\n', StringComparison.Ordinal) || v.Contains('\r', StringComparison.Ordinal)))
        {
            // "CLI basics": ? and Tab cannot be pasted into the CLI Console or some SSH clients.
            t.FieldItems.Add(Item(path, objectName, RebuildCodes.UnpastableCharacter, [param], Mask(f.Statement.Raw), null));
            return;
        }

        t.Entry.Sets.Add(new CliSet(param, values, IsQuoted(p)));
    }

    private void TranslateTable(string path, IReadOnlyList<string> sub, SchemaNode node, ConfigBlock b, string objectName, ValueHook? hook, Translation t)
    {
        var rule = _rules.ForTable(path, sub, b.Path);
        if (rule is not null)
        {
            _applied.Add(new AppliedRule(rule.Id, path, objectName, b.Path));
            if (rule.Kind != "skip") t.FieldItems.Add(Item(path, objectName, RebuildCodes.RemovedNoEquivalent, ["config " + b.Path, rule.Note], Mask(b.ToText()), rule));
            return;
        }

        if (!node.Sub.TryGetValue(b.Path, out var subNode))
        {
            t.FieldItems.Add(Item(path, objectName, RebuildCodes.ParamNotInTarget, ["config " + b.Path, _target.Version], Mask(b.ToText()), null));
            return;
        }

        var subPath = sub.Append(b.Path).ToList();
        var block = new CliBlock(b.Path, subNode.IsTable);
        if (subNode.IsTable)
        {
            foreach (var e in b.Entries)
            {
                var inner = Translate(path, subPath, e, subNode, e.Key, objectName, hook);
                if (inner.Fatal is { } fatal)
                {
                    t.FieldItems.Add(Item(path, objectName, fatal.Code, fatal.Args, Mask(e.ToText()), null));
                    continue;
                }

                t.FieldItems.AddRange(inner.FieldItems);
                block.Entries.Add(inner.Entry);
            }
        }
        else
        {
            var inner = Translate(path, subPath, b, subNode, null, objectName, hook);
            t.FieldItems.AddRange(inner.FieldItems);
            if (inner.Fatal is null) block.Entries.Add(inner.Entry);
        }

        if (block.Entries.Count > 0) t.Entry.Subs.Add(block);
    }

    private IReadOnlyList<string> Resolve(Ref kind, IReadOnlyList<string> values, out string? missing)
    {
        missing = null;
        var result = new List<string>(values.Count);
        foreach (var v in values)
        {
            var name = kind == Ref.Interface ? _ifMap.GetValueOrDefault(v, v) : v;
            var ok = kind switch
            {
                Ref.Interface => _known[Ref.Interface].Contains(name),
                Ref.AddressOrVip => _known[Ref.Address].Contains(name) || _known[Ref.Vip].Contains(name),
                Ref.UserOrServer => _known[Ref.User].Contains(name) || _known[Ref.Ldap].Contains(name) || _known[Ref.Radius].Contains(name),
                _ => _known[kind].Contains(name),
            };
            if (!ok)
            {
                missing = v;
                return values;
            }

            result.Add(name);
        }

        return result;
    }

    private static bool IsQuoted(SchemaParam p) => p.Type is "string" or "var-string" or "password" or "password-2" or "password-3" or "password_aes256";

    private ManualItem Item(string path, string obj, string code, IReadOnlyList<string> args, string original, TranslationRule? rule) =>
        new(path, obj, code, args, original, rule?.Id, rule?.Sources ?? []);

    /// <summary>Replaces ENC values with a placeholder: manual items and reports never contain them.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\sENC\s")]
    private static partial System.Text.RegularExpressions.Regex EncToken();

    internal static string Mask(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var enc = EncToken().Match(lines[i]);
            if (enc.Success)
            {
                lines[i] = lines[i][..(enc.Index + enc.Length)] + "********";
                continue;
            }

            var words = lines[i].TrimStart().Split([' ', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 3 && words[0] == "set" && SecretParams.Contains(words[1]))
            {
                lines[i] = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)] + "set " + words[1] + " ********";
            }
        }

        return string.Join('\n', lines).TrimEnd('\n');
    }

    // ------------------------------------------------------------------ simple tables

    /// <summary>Rebuilds every entry of a table (or a non-table block) with generic translation.</summary>
    private void Simple(ConfigDocument doc, string path, string section, Ref? refKind = null, Ref? register = null, Func<string, bool>? skip = null, ValueHook? hook = null)
    {
        var node = _target.Find(path);
        foreach (var block in BlocksOf(doc, path))
        {
            if (node is null)
            {
                Count(path, source: 1, manual: 1);
                _manual.Add(Item(path, path, RebuildCodes.PathNotInTarget, [path, _target.Version], Mask(block.ToText()), null));
                continue;
            }

            if (!node.IsTable)
            {
                var t = Translate(path, [], block, node, null, path, hook ?? DefaultHook(path));
                Count(path, source: 1, rebuilt: t.Fatal is null ? 1 : 0, manual: t.Fatal is null ? 0 : 1);
                Emit(section, path, t, path, block);
                continue;
            }

            foreach (var e in block.Entries)
            {
                Count(path, source: 1);
                if (skip?.Invoke(e.Key) == true)
                {
                    Count(path, converted: 1);
                    continue;
                }

                var t = Translate(path, [], e, node, e.Key, e.Key, hook ?? DefaultHook(path));
                if (Emit(section, path, t, e.Key, e))
                {
                    Count(path, rebuilt: 1);
                    if (register is { } r) _known[r].Add(e.Key);
                }
                else
                {
                    Count(path, manual: 1);
                }
            }
        }
    }

    /// <summary>Like <see cref="Simple"/> for groups whose members can be groups: members first.</summary>
    private void Ordered(ConfigDocument doc, string path, string section, Ref register, string memberParam)
    {
        var entries = BlocksOf(doc, path).SelectMany(b => b.Entries).ToList();
        var byName = new Dictionary<string, List<EditEntry>>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!byName.TryGetValue(e.Key, out var list)) byName[e.Key] = list = [];
            list.Add(e);
        }

        // Kahn's algorithm: a group is written after the groups it contains.
        var waiting = new Dictionary<EditEntry, int>();
        var users = new Dictionary<string, List<EditEntry>>(StringComparer.Ordinal);
        var ready = new Queue<EditEntry>();
        foreach (var e in entries)
        {
            var deps = (e.Field(memberParam)?.Values ?? []).Where(m => m != e.Key && byName.ContainsKey(m)).Distinct(StringComparer.Ordinal).ToList();
            waiting[e] = deps.Count;
            foreach (var d in deps)
            {
                if (!users.TryGetValue(d, out var u)) users[d] = u = [];
                u.Add(e);
            }

            if (deps.Count == 0) ready.Enqueue(e);
        }

        var done = new HashSet<EditEntry>();
        while (ready.Count > 0)
        {
            var e = ready.Dequeue();
            done.Add(e);
            Count(path, source: 1);
            var t = Translate(path, [], e, _target.Find(path)!, e.Key, e.Key, DefaultHook(path));
            if (Emit(section, path, t, e.Key, e))
            {
                Count(path, rebuilt: 1);
                _known[register].Add(e.Key);
            }
            else
            {
                Count(path, manual: 1);
            }

            if (byName[e.Key].All(done.Contains) && users.TryGetValue(e.Key, out var dependents))
            {
                foreach (var u in dependents)
                {
                    if (--waiting[u] == 0) ready.Enqueue(u);
                }
            }
        }

        foreach (var e in entries.Where(e => !done.Contains(e)))
        {
            Count(path, source: 1, manual: 1);
            _manual.Add(Item(path, e.Key, RebuildCodes.CircularGroup, [e.Key], Mask(e.ToText()), null));
        }
    }

    /// <summary>Adds the translated entry to the script, or the whole object to the manual list.</summary>
    private bool Emit(string section, string path, Translation t, string objectName, ConfigNode original)
    {
        if (t.Fatal is null && t.Entry.Key is { } key)
        {
            var maxName = _target.Find(path)?.Params.GetValueOrDefault("name")?.MaxLength;
            if (key.Length == 0 || key.Any(c => c is '?' or '\t' or '\n' or '\r') || (maxName is { } max && !key.All(char.IsAsciiDigit) && key.Length > max))
            {
                t.Fatal = (RebuildCodes.InvalidKey, [key.Replace("\t", "TAB", StringComparison.Ordinal)]);
            }
            else if (!_emittedKeys.Add((path, key)))
            {
                t.Fatal = (RebuildCodes.DuplicateKey, [key]);
            }
        }

        _manual.AddRange(t.FieldItems);
        if (t.Fatal is { } fatal)
        {
            _manual.Add(Item(path, objectName, fatal.Code, fatal.Args, Mask(original.ToText()), null));
            return false;
        }

        BlockIn(section, path).Entries.Add(t.Entry);
        return true;
    }

    // ------------------------------------------------------------------ secrets

    private ValueHook? DefaultHook(string path) => (param, values, t) =>
    {
        if (values.Count == 0) return values;
        var category = (path, param) switch
        {
            ("system admin", "password") => SecretCategory.Admin,
            ("user local", "passwd") => SecretCategory.LocalUser,
            ("vpn ipsec phase1-interface", "psksecret") => SecretCategory.VpnPsk,
            ("user ldap", "password") or ("user radius", "secret") or ("user radius", "secondary-secret") or ("user radius", "tertiary-secret") => SecretCategory.Server,
            ("system interface", "password") => SecretCategory.Pppoe,
            _ => SecretCategory.Other,
        };
        var hidden = values[0] == "ENC" || values[0] == ConfigInspector.PasswordMaskToken;
        if (!hidden)
        {
            if (category == SecretCategory.Other && !SecretParams.Contains(param)) return values;

            // A secret written in clear in the source: kept (it is the real one), but treated as a secret everywhere.
            _secrets.Add(new SecretAssignment(category, path, t.Entry.Key ?? path, param, string.Join(' ', values), false));
            t.Entry.Sets.Add(new CliSet(param, values, QuoteValues: true, Secret: true));
            return null;
        }

        var password = Password(category);
        var obj = t.Entry.Key ?? path;
        if (password is null)
        {
            // Without the password the object is unusable or unsafe: keep it out of the script.
            if (category is SecretCategory.Admin or SecretCategory.VpnPsk or SecretCategory.LocalUser)
            {
                t.Fatal = (RebuildCodes.SecretRequired, [param, CategoryName(category)]);
            }
            else
            {
                t.FieldItems.Add(Item(path, obj, RebuildCodes.SecretRequired, [param, CategoryName(category)], $"set {param} ENC ********", null));
            }

            return null;
        }

        _secrets.Add(new SecretAssignment(category, path, obj, param, password, !_req.Secrets.Provided.ContainsKey(category)));
        t.Entry.Sets.Add(new CliSet(param, [password], QuoteValues: true, Secret: true));
        return null;
    };

    private string? Password(SecretCategory category)
    {
        if (_req.Secrets.Provided.TryGetValue(category, out var p) && !string.IsNullOrEmpty(p)) return p;
        if (!_req.Secrets.Generate || !SecretPlan.CanGenerate(category)) return null;
        if (!_generated.TryGetValue(category, out var g))
        {
            g = CliPassword.Generate();
            _generated[category] = g;
        }

        return g;
    }

    internal static string CategoryName(SecretCategory c) => c switch
    {
        SecretCategory.Admin => "admin",
        SecretCategory.LocalUser => "local-user",
        SecretCategory.VpnPsk => "vpn-psk",
        SecretCategory.Server => "ldap-radius",
        SecretCategory.Pppoe => "pppoe",
        _ => "other",
    };

    // ------------------------------------------------------------------ helpers

    private IEnumerable<string> AccProfilesOnTarget()
    {
        // super_admin is built in on every FortiOS version; others come from the factory-default backup when available.
        yield return "super_admin";
        var d = _req.Target.DefaultBackup;
        if (d is null) yield break;
        foreach (var b in d.GlobalScope.Blocks.Where(b => b.Path == "system accprofile"))
        {
            foreach (var e in b.Entries) yield return e.Key;
        }
    }

    internal static bool TryParseAddress(string? text, out IPAddress ip, out int prefix)
    {
        ip = IPAddress.None;
        prefix = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        string ipPart;
        if (s.Contains('/', StringComparison.Ordinal))
        {
            var parts = s.Split('/');
            ipPart = parts[0];
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix) || prefix is < 0 or > 32) return false;
        }
        else
        {
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[1], out var mask) || !MaskToPrefix(mask, out prefix)) return false;
            ipPart = parts[0];
        }

        return IPAddress.TryParse(ipPart, out ip!) && ip.GetAddressBytes().Length == 4 && ipPart.Count(c => c == '.') == 3;
    }

    internal static bool MaskToPrefix(IPAddress mask, out int prefix)
    {
        var b = mask.GetAddressBytes();
        var bits = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        prefix = System.Numerics.BitOperations.PopCount(bits);
        return bits == (prefix == 0 ? 0 : uint.MaxValue << (32 - prefix));
    }

    internal static string PrefixToMask(int prefix)
    {
        var bits = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return new IPAddress([(byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits]).ToString();
    }

    internal static bool SameSubnet(IPAddress a, IPAddress b, int prefix)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        var ua = (uint)(x[0] << 24 | x[1] << 16 | x[2] << 8 | x[3]);
        var ub = (uint)(y[0] << 24 | y[1] << 16 | y[2] << 8 | y[3]);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return (ua & mask) == (ub & mask);
    }
}
