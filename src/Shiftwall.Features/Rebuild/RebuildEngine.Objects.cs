using System.Globalization;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

public sealed partial class RebuildEngine
{
    private static readonly HashSet<string> PhysicalTypes = new(StringComparer.Ordinal) { "physical", "hard-switch", "switch" };
    private static readonly HashSet<string> CreatedTypes = new(StringComparer.Ordinal) { "vlan", "loopback", "aggregate", "redundant" };

    /// <summary>Parameters of an existing target interface that describe the old hardware or its kind: never written.</summary>
    private static readonly HashSet<string> ExistingInterfaceDrop = new(StringComparer.Ordinal) { "type", "speed", "interface", "member" };

    private static readonly HashSet<string> AddressingParams = new(StringComparer.Ordinal)
    {
        "mode", "ip", "dhcp-relay-service", "dhcp-relay-ip", "defaultgw", "dns-server-override", "username", "password",
        "idle-timeout", "distance", "priority", "ipunnumbered", "pppoe-unnumbered-negotiate", "disc-retry-timeout", "padt-retry-timeout",
    };

    // ------------------------------------------------------------------ interfaces

    private void PlanInterfaces(ConfigDocument doc, SslVpnInfo ssl)
    {
        foreach (var p in _req.Interfaces)
        {
            if (!string.IsNullOrWhiteSpace(p.Target)) _ifMap[p.Source] = p.Target!;
        }

        foreach (var b in BlocksOf(doc, "system interface"))
        {
            foreach (var e in b.Entries)
            {
                var type = e.Field("type")?.Values.FirstOrDefault() ?? InterfaceInventory.DefaultType;
                if (CreatedTypes.Contains(type) && !_ifMap.ContainsKey(e.Key)) _ifMap[e.Key] = e.Key;
            }
        }

        foreach (var b in BlocksOf(doc, "vpn ipsec phase1-interface"))
        {
            foreach (var e in b.Entries) _ifMap.TryAdd(e.Key, e.Key);
        }

        foreach (var b in BlocksOf(doc, "system zone"))
        {
            foreach (var e in b.Entries) _ifMap.TryAdd(e.Key, e.Key);
        }

        if (ssl.Present && _opt.ConvertSslVpn) _ifMap["ssl.root"] = _opt.DialupTunnelName;

        var targets = _ifMap.Values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var t in targets) _findings.Add(MigrationFinding.Error(RebuildCodes.DuplicateTarget, t));
    }

    /// <summary>Removes from a target switch the ports that the plan uses as interfaces of their own.</summary>
    private void RebuildSwitchPorts()
    {
        foreach (var sw in _req.Target.Switches)
        {
            var taken = sw.Members.Where(m => _ifMap.ContainsValue(m)).ToList();
            if (taken.Count == 0) continue;
            if (taken.Count >= sw.Members.Count)
            {
                _findings.Add(MigrationFinding.Error(RebuildCodes.SwitchWouldBeEmpty, sw.Name));
                continue;
            }

            var entry = new CliEntry(sw.Name);
            var ports = new CliBlock("port", true);
            ports.Deletes.AddRange(taken);
            entry.Subs.Add(ports);
            BlockIn("switch", "system virtual-switch").Entries.Add(entry);
            _findings.Add(MigrationFinding.Warning(RebuildCodes.PortsRemovedFromSwitch, string.Join(", ", taken), sw.Name));
            foreach (var t in taken) _known[Ref.Interface].Add(t);
        }
    }

    private void RebuildInterfaces(ConfigDocument doc, SslVpnInfo ssl)
    {
        const string path = "system interface";
        var node = _target.Find(path)!;
        var entries = BlocksOf(doc, path).SelectMany(b => b.Entries).ToList();
        var phase1 = BlocksOf(doc, "vpn ipsec phase1-interface").SelectMany(b => b.Entries).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        // Created interfaces need their parent first: physical, then aggregate/redundant, then VLAN and loopback.
        int Rank(EditEntry e) => (e.Field("type")?.Values.FirstOrDefault() ?? InterfaceInventory.DefaultType) switch
        {
            "aggregate" or "redundant" => 1,
            "vlan" or "loopback" => 2,
            _ => 0,
        };

        foreach (var e in entries.OrderBy(Rank))
        {
            var type = e.Field("type")?.Values.FirstOrDefault() ?? InterfaceInventory.DefaultType;
            Count(path, source: 1);
            if (e.Key == "ssl.root" || (type == "tunnel" && !phase1.Contains(e.Key)))
            {
                if (e.Key == "ssl.root" && ssl.Present && _opt.ConvertSslVpn) Count(path, converted: 1);
                else Manual(path, e, RebuildCodes.UnsupportedInterfaceType, type);
                continue;
            }

            if (type == "tunnel")
            {
                Count(path, -1); // counted with the IPsec tunnel it belongs to
                continue;
            }

            if (PhysicalTypes.Contains(type))
            {
                if (!_ifMap.TryGetValue(e.Key, out var target) || !_known[Ref.Interface].Contains(target))
                {
                    var used = e.Field("ip") is not null || e.Field("mode")?.Values.FirstOrDefault() is "dhcp" or "pppoe";
                    _manual.Add(Item(path, e.Key, used ? RebuildCodes.InterfaceNotMapped : RebuildCodes.InterfaceNotMappedUnused, [e.Key], Mask(e.ToText()), null));
                    Count(path, manual: 1);
                    continue;
                }

                var t = Translate(path, [], e, node, target, e.Key, DefaultHook(path), ExistingInterfaceDrop.Union(AddressingParams).ToHashSet());
                Addressing(e, target, t);
                var section = string.Equals(target, _opt.ManagementInterface, StringComparison.Ordinal) ? "management" : "interfaces";
                if (Emit(section, path, t, e.Key, e))
                {
                    Count(path, rebuilt: 1);
                    if (section == "management") ManagementChecks(target, t.Entry);
                }
                else
                {
                    Count(path, manual: 1);
                }

                continue;
            }

            if (CreatedTypes.Contains(type))
            {
                if (_req.Target.Interfaces.Contains(e.Key, StringComparer.Ordinal))
                {
                    Manual(path, e, RebuildCodes.NameTakenOnTarget, e.Key);
                    continue;
                }

                var t = Translate(path, [], e, node, e.Key, e.Key, DefaultHook(path), AddressingParams);
                Addressing(e, e.Key, t);
                if (Emit("interfaces", path, t, e.Key, e))
                {
                    Count(path, rebuilt: 1);
                    _known[Ref.Interface].Add(e.Key);
                }
                else
                {
                    Count(path, manual: 1);
                }

                continue;
            }

            Manual(path, e, RebuildCodes.UnsupportedInterfaceType, type);
        }
    }

    private void Manual(string path, EditEntry e, string code, params string[] args)
    {
        _manual.Add(Item(path, e.Key, code, args, Mask(e.ToText()), null));
        Count(path, manual: 1);
    }

    /// <summary>Writes mode and address of an interface: kept from the source or as chosen in the plan.</summary>
    private void Addressing(EditEntry e, string target, Translation t)
    {
        if (t.Fatal is not null) return;
        var plan = _req.Interfaces.FirstOrDefault(p => p.Source == e.Key);
        var sets = t.Entry.Sets;
        var at = sets.FindIndex(s => s.Param == "vdom") + 1;
        void Put(string param, IReadOnlyList<string> values, bool quote = false) => sets.Insert(at++, new CliSet(param, values, quote));

        var mode = plan?.Mode ?? AddressingMode.KeepSource;
        if (mode == AddressingMode.KeepSource)
        {
            var srcMode = e.Field("mode")?.Values.FirstOrDefault() ?? "static";
            switch (srcMode)
            {
                case "static":
                    if (e.Field("ip") is { } ip)
                    {
                        // The target may use DHCP on this port out of the box (e.g. the WAN port): the mode is written explicitly.
                        Put("mode", ["static"]);
                        Put("ip", ip.Values);
                        _readdressed.Add(target);
                    }

                    break;
                case "dhcp":
                    Put("mode", ["dhcp"]);
                    foreach (var p in new[] { "dns-server-override", "priority" })
                    {
                        if (e.Field(p) is { } f && _target.Find("system interface")!.Params.ContainsKey(p)) Put(p, f.Values);
                    }

                    LearnedRoute(e, Put);
                    break;
                case "pppoe":
                    Put("mode", ["pppoe"]);
                    LearnedRoute(e, Put);
                    if (e.Field("username") is { } u) Put("username", u.Values, quote: true);
                    var pw = Password(SecretCategory.Pppoe);
                    if (pw is null)
                    {
                        t.FieldItems.Add(Item("system interface", e.Key, RebuildCodes.SecretRequired, ["password", CategoryName(SecretCategory.Pppoe)], "set password ENC ********", null));
                    }
                    else
                    {
                        sets.Insert(at++, new CliSet("password", [pw], true, Secret: true));
                        _secrets.Add(new SecretAssignment(SecretCategory.Pppoe, "system interface", target, "password", pw, false));
                    }

                    break;
                default:
                    t.FieldItems.Add(Item("system interface", e.Key, RebuildCodes.InvalidValue, ["mode", srcMode, _target.Version], Mask(e.Field("mode")!.Statement.Raw), null));
                    break;
            }

            if (e.Field("dhcp-relay-service") is { } relay)
            {
                sets.Add(new CliSet("dhcp-relay-service", relay.Values, false));
                if (e.Field("dhcp-relay-ip") is { } rip) sets.Add(new CliSet("dhcp-relay-ip", rip.Values, true));
            }

            return;
        }

        switch (mode)
        {
            case AddressingMode.Static:
                if (!TryParseAddress(plan!.Address, out var addr, out var prefix))
                {
                    t.Fatal = (RebuildCodes.InvalidPlanAddress, [e.Key, plan.Address ?? ""]);
                    return;
                }

                Put("mode", ["static"]);
                Put("ip", [addr.ToString(), PrefixToMask(prefix)]);
                _readdressed.Add(target);
                break;
            case AddressingMode.Dhcp:
                Put("mode", ["dhcp"]);
                break;
            case AddressingMode.Pppoe:
                Put("mode", ["pppoe"]);
                break;
        }
    }

    /// <summary>
    /// DHCP/PPPoE: the default gateway learned from the server, with the 5.2 defaults written explicitly
    /// (rules D010 defaultgw disable, D011 distance 1).
    /// </summary>
    private void LearnedRoute(EditEntry e, Action<string, IReadOnlyList<string>, bool> put)
    {
        var gw = e.Field("defaultgw")?.Values.FirstOrDefault();
        if (gw is null)
        {
            gw = "disable";
            _applied.Add(new AppliedRule("D010", "system interface", e.Key, "defaultgw"));
        }

        put("defaultgw", [gw], false);
        if (gw != "enable") return;
        var distance = e.Field("distance")?.Values;
        if (distance is null)
        {
            distance = ["1"];
            _applied.Add(new AppliedRule("D011", "system interface", e.Key, "distance"));
        }

        put("distance", distance, false);
    }

    private void ManagementChecks(string target, CliEntry entry)
    {
        var access = entry.Sets.FirstOrDefault(s => s.Param == "allowaccess")?.Values ?? [];
        if (!access.Contains("https") && !access.Contains("ssh")) _findings.Add(MigrationFinding.Error(RebuildCodes.ManagementLockout, target));
        _findings.Add(MigrationFinding.Warning(RebuildCodes.ManagementLast, target));
    }

    // ------------------------------------------------------------------ IPsec

    private void RebuildIpsec(ConfigDocument doc)
    {
        Simple(doc, "vpn ipsec phase1-interface", "ipsec", register: Ref.Phase1);
        foreach (var name in _known[Ref.Phase1]) _known[Ref.Interface].Add(name);

        // Settings of the tunnel interfaces (created by phase1-interface): after the tunnels.
        const string path = "system interface";
        var node = _target.Find(path)!;
        foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries).Where(e => e.Field("type")?.Values.FirstOrDefault() == "tunnel" && _known[Ref.Phase1].Contains(e.Key)))
        {
            Count(path, source: 1);
            var t = Translate(path, [], e, node, e.Key, e.Key, DefaultHook(path), new HashSet<string>(StringComparer.Ordinal) { "type", "interface" });
            if (t.Entry.Sets.Count == 0 && t.Entry.Subs.Count == 0 && t.Fatal is null)
            {
                Count(path, rebuilt: 1);
                continue;
            }

            if (Emit("ipsec", path, t, e.Key, e)) Count(path, rebuilt: 1);
            else Count(path, manual: 1);
        }

        foreach (var b in BlocksOf(doc, "vpn ipsec phase1"))
        {
            foreach (var e in b.Entries) Manual("vpn ipsec phase1", e, RebuildCodes.PolicyBasedVpn, e.Key);
        }

        Simple(doc, "vpn ipsec phase2-interface", "ipsec");
        if (_known[Ref.Phase1].Count > 0) _findings.Add(MigrationFinding.Warning(RebuildCodes.IpsecPeerCheck));
    }

    // ------------------------------------------------------------------ routes and policies

    private void RebuildRoutes(ConfigDocument doc)
    {
        Simple(doc, "router static", "routes");

        // Gateways typed in the plan for static interfaces become default routes.
        var routes = BlockIn("routes", "router static");
        var ids = BlocksOf(doc, "router static").SelectMany(b => b.Entries).Select(e => int.TryParse(e.Key, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        foreach (var p in _req.Interfaces.Where(p => p.Mode == AddressingMode.Static && !string.IsNullOrWhiteSpace(p.Gateway) && p.Target is not null))
        {
            if (!TryParseAddress(p.Address, out var addr, out var prefix) || !System.Net.IPAddress.TryParse(p.Gateway, out var gw) || !SameSubnet(addr, gw, prefix))
            {
                _findings.Add(MigrationFinding.Error(RebuildCodes.GatewayOutsideSubnet, p.Gateway!, p.Target!));
                continue;
            }

            var existing = routes.Entries.FirstOrDefault(r =>
                r.Sets.Any(s => s.Param == "device" && s.Values.SequenceEqual([p.Target!])) &&
                !r.Sets.Any(s => s.Param == "dst" && !s.Values.SequenceEqual(["0.0.0.0", "0.0.0.0"])));
            if (existing is not null)
            {
                existing.Sets.RemoveAll(s => s.Param == "gateway");
                existing.Sets.Add(new CliSet("gateway", [p.Gateway!], false));
            }
            else
            {
                var entry = new CliEntry((++ids).ToString(CultureInfo.InvariantCulture), quoteKey: false);
                entry.Sets.Add(new CliSet("gateway", [p.Gateway!], false));
                entry.Sets.Add(new CliSet("device", [p.Target!], true));
                routes.Entries.Add(entry);
            }
        }

        foreach (var plan in _req.Interfaces.Where(p => p.Mode == AddressingMode.Static && p.NameServers is { Count: > 0 }).Take(1))
        {
            var entry = new CliEntry(null);
            entry.Sets.Add(new CliSet("primary", [plan.NameServers![0]], false));
            if (plan.NameServers.Count > 1) entry.Sets.Add(new CliSet("secondary", [plan.NameServers[1]], false));
            BlockIn("dns", "system dns").Entries.Add(entry);
        }
    }

    private void RebuildPolicies(ConfigDocument doc, SslVpnInfo ssl)
    {
        const string path = "firewall policy";
        var node = _target.Find(path)!;
        foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries))
        {
            Count(path, source: 1);
            if (ssl.Present && _opt.ConvertSslVpn && ssl.Policies.Contains(e))
            {
                Count(path, converted: 1);
                if (_convertedPolicies.TryGetValue(e, out var converted)) BlockIn("policies", path).Entries.Add(converted);
                continue;
            }

            if (e.Field("action")?.Values.FirstOrDefault() == "ipsec")
            {
                Manual(path, e, RebuildCodes.PolicyBasedVpn, e.Key);
                continue;
            }

            var profiles = new List<string>();
            ValueHook hook = (param, values, t) =>
            {
                if (param == "utm-status") return null; // written below only if a profile is kept
                if (!ProfileParams.Contains(param)) return DefaultHook(path)!(param, values, t);
                if (ProfileTables.TryGetValue(param, out var table) && values.Count == 1 && ExistsOnTarget(table, values[0])) return values;
                profiles.Add($"{param} {string.Join(' ', values)}");
                return null;
            };
            var t = Translate(path, [], e, node, e.Key, e.Key, hook);
            if (t.Fatal is null && t.Entry.Sets.Any(s => ProfileParams.Contains(s.Param) && s.Param is not ("profile-protocol-options" or "ssl-ssh-profile")))
            {
                t.Entry.Sets.Add(new CliSet("utm-status", ["enable"], false));
            }

            if (profiles.Count > 0 && t.Fatal is null)
            {
                t.FieldItems.Add(Item(path, e.Key, RebuildCodes.ProfilesNotMigrated, [string.Join("; ", profiles)], Mask(e.ToText()), _rules.Rules.First(r => r.Id == "E002")));
            }

            if (Emit("policies", path, t, e.Key, e)) Count(path, rebuilt: 1);
            else Count(path, manual: 1);
        }
    }

    private bool ExistsOnTarget(string table, string name) =>
        _req.Target.DefaultBackup?.Blocks.Where(b => b.Path == table).SelectMany(b => b.Entries).Any(e => e.Key == name) == true;

    // ------------------------------------------------------------------ admins and DHCP

    private void RebuildAdmins(ConfigDocument doc)
    {
        const string path = "system admin";
        var node = _target.Find(path)!;
        foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries))
        {
            Count(path, source: 1);
            if (string.Equals(e.Key, _opt.CurrentAdmin, StringComparison.Ordinal))
            {
                // Administration Guide 8.0.2, "Default administrator password": changing the password of the account in use
                // asks for the current password, which a script cannot answer.
                Manual(path, e, RebuildCodes.CurrentAdmin, e.Key);
                continue;
            }

            var t = Translate(path, [], e, node, e.Key, e.Key, DefaultHook(path));
            if (t.Fatal is null && !t.Entry.Sets.Any(s => s.Param == "accprofile")) t.Fatal = (RebuildCodes.UnresolvedReference, ["accprofile", "-"]);
            if (Emit("admins", path, t, e.Key, e)) Count(path, rebuilt: 1);
            else Count(path, manual: 1);
        }
    }

    private void RebuildDhcp(ConfigDocument doc)
    {
        const string path = "system dhcp server";
        var node = _target.Find(path)!;
        var defaults = _req.Target.DefaultBackup is { } d
            ? d.Blocks.Where(b => b.Path == path).SelectMany(b => b.Entries).Select(x => (Id: x.Key, Interface: x.Field("interface")?.Values.FirstOrDefault())).ToList()
            : null;
        if (defaults is null) _findings.Add(MigrationFinding.Warning(RebuildCodes.FactoryDhcpUnknown));

        // Factory DHCP servers on interfaces that get a new address are removed before the address changes.
        foreach (var (id, intf) in defaults ?? [])
        {
            if (intf is null || !_readdressed.Contains(intf)) continue;
            var section = intf == _opt.ManagementInterface ? "management" : "dhcp";
            var block = intf == _opt.ManagementInterface ? new CliBlock(path, true) : BlockIn(section, path);
            block.Deletes.Add(id);
            if (intf == _opt.ManagementInterface) _sections["management"].Blocks.Insert(0, block);
        }

        var usedIds = new HashSet<string>(defaults?.Where(x => x.Interface is null || !_readdressed.Contains(x.Interface)).Select(x => x.Id) ?? [], StringComparer.Ordinal);
        var next = BlocksOf(doc, path).SelectMany(b => b.Entries).Select(e => int.TryParse(e.Key, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Concat(usedIds.Select(x => int.TryParse(x, CultureInfo.InvariantCulture, out var n) ? n : 0)).DefaultIfEmpty(0).Max();
        foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries))
        {
            Count(path, source: 1);
            var key = usedIds.Contains(e.Key) ? (++next).ToString(CultureInfo.InvariantCulture) : e.Key;
            var t = Translate(path, [], e, node, key, e.Key, DefaultHook(path));
            var intf = t.Entry.Sets.FirstOrDefault(s => s.Param == "interface")?.Values.FirstOrDefault();
            var section = intf is not null && intf == _opt.ManagementInterface ? "management-dhcp" : "dhcp";
            if (Emit(section, path, t, e.Key, e)) Count(path, rebuilt: 1);
            else Count(path, manual: 1);
        }
    }

    // ------------------------------------------------------------------ report

    private static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
    {
        "system global", "system interface", "system zone", "system dns", "system dhcp server", "system admin", "system virtual-switch",
        "firewall address", "firewall addrgrp", "firewall service category", "firewall service custom", "firewall service group",
        "firewall schedule onetime", "firewall schedule recurring", "firewall schedule group", "firewall vip", "firewall vipgrp",
        "firewall ippool", "user local", "user group", "user ldap", "user radius", "vpn ipsec phase1-interface",
        "vpn ipsec phase2-interface", "vpn ipsec phase1", "router static", "firewall policy",
    };

    private void ReportOtherSections(ConfigDocument doc, SslVpnInfo ssl)
    {
        foreach (var g in doc.Blocks.GroupBy(b => b.Path).Where(g => !Handled.Contains(g.Key)))
        {
            var n = g.Sum(b => Math.Max(1, b.Entries.Count()));
            if (ssl.Present && _opt.ConvertSslVpn && g.Key.StartsWith("vpn ssl", StringComparison.Ordinal))
            {
                Count(g.Key, source: n, converted: n);
                continue;
            }

            Count(g.Key, source: n, manual: n);
            _manual.Add(Item(g.Key, g.Key, RebuildCodes.SectionNotRebuilt, [g.Key, n.ToString(CultureInfo.InvariantCulture)], "", null));
        }
    }

    private void AddFindings(SslVpnInfo ssl)
    {
        _findings.Add(MigrationFinding.Info(RebuildCodes.FactoryDefaultTarget));
        if (_opt.ManagementInterface is null) _findings.Add(MigrationFinding.Warning(RebuildCodes.ManagementUnknown));
        if (_req.Target.LowMemory) _findings.Add(MigrationFinding.Warning(RebuildCodes.LowMemoryModel, _req.Target.ModelId ?? "?"));
        if (_secrets.Count > 0) _findings.Add(MigrationFinding.Warning(RebuildCodes.SecretsReset, _secrets.Count.ToString(CultureInfo.InvariantCulture)));
        if (_req.Target.DefaultBackup is null) _findings.Add(MigrationFinding.Warning(RebuildCodes.NoDefaultBackup));
        if (_known[Ref.Radius].Count > 0 && _target.Find("user radius")?.Params.ContainsKey("require-message-authenticator") == true)
        {
            _findings.Add(MigrationFinding.Warning(RebuildCodes.RadiusMessageAuthenticator, string.Join(", ", _known[Ref.Radius])));
        }
    }
}
