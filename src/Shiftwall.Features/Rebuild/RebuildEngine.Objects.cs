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

        // VLANs, loopbacks, aggregates, tunnels and zones share one name space with the target interfaces.
        var created = BlocksOf(doc, "system interface").SelectMany(b => b.Entries)
            .Where(e => CreatedTypes.Contains(e.Field("type")?.Values.FirstOrDefault() ?? InterfaceInventory.DefaultType)).Select(e => e.Key)
            .Concat(BlocksOf(doc, "vpn ipsec phase1-interface").SelectMany(b => b.Entries).Select(e => e.Key))
            .Concat(BlocksOf(doc, "system zone").SelectMany(b => b.Entries).Select(e => e.Key))
            .Concat(ssl.Present && _opt.ConvertSslVpn ? [_opt.DialupTunnelName] : []);
        foreach (var clash in created.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            _findings.Add(MigrationFinding.Error(RebuildCodes.DuplicateTarget, clash));
        }

        var targets = _ifMap.Values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var t in targets) _findings.Add(MigrationFinding.Error(RebuildCodes.DuplicateTarget, t));
    }

    /// <summary>
    /// Switches and ports of the target (section 6b): ports leave their factory switches first, then the ports that go
    /// into a switch are set to static with IP 0.0.0.0/0.0.0.0, then switches are created or get their new ports.
    /// Everything that uses a switch comes later in the script.
    /// </summary>
    private void RebuildSwitches(ConfigDocument doc)
    {
        _layout = SwitchPlanner.Plan(_req.Target, _req.Interfaces, _req.NewInterfaces ?? []);
        _findings.AddRange(_layout.Findings);
        CountSourceSwitches(doc);
        if (_layout.HasErrors) return;

        foreach (var c in _layout.Changes.Where(c => c.Existing && c.Removed.Count > 0))
        {
            var entry = new CliEntry(c.Name);
            if (c.Hardware)
            {
                var ports = new CliBlock("port", true);
                ports.Deletes.AddRange(c.Removed);
                entry.Subs.Add(ports);
                BlockIn("switch", "system virtual-switch").Entries.Add(entry);
            }
            else
            {
                entry.Sets.Add(new CliSet("member", [.. c.Remaining.Except(c.Ports)], true));
                BlockIn("switch", "system switch-interface").Entries.Add(entry);
            }

            foreach (var r in c.Removed) _known[Ref.Interface].Add(r);
        }

        foreach (var port in _layout.Prepared)
        {
            var entry = new CliEntry(port);
            entry.Sets.Add(new CliSet("mode", ["static"], false));
            entry.Sets.Add(new CliSet("ip", ["0.0.0.0", "0.0.0.0"], false));
            BlockIn("switch", "system interface").Entries.Add(entry);
        }

        foreach (var c in _layout.Changes.Where(c => c.Ports.Count > 0))
        {
            var entry = new CliEntry(c.Name);
            if (c.Hardware)
            {
                if (!c.Existing && c.PhysicalSwitch is { } ps) entry.Sets.Add(new CliSet("physical-switch", [ps], true));
                var ports = new CliBlock("port", true);
                foreach (var p in c.Ports) ports.Entries.Add(new CliEntry(p));
                entry.Subs.Add(ports);
                BlockIn("switch", "system virtual-switch").Entries.Add(entry);
            }
            else
            {
                if (!c.Existing) entry.Sets.Add(new CliSet("vdom", ["root"], true));
                entry.Sets.Add(new CliSet("member", [.. c.Remaining], true));
                if (!c.Existing) entry.Sets.Add(new CliSet("type", ["switch"], false));
                BlockIn("switch", "system switch-interface").Entries.Add(entry);
            }

            _known[Ref.Interface].Add(c.Name);
        }

        foreach (var v in _layout.Vlans) _known[Ref.Interface].Add(v.Name);
    }

    /// <summary>Source switch tables: carried over when the switch interface is mapped, otherwise listed as manual work.</summary>
    private void CountSourceSwitches(ConfigDocument doc)
    {
        foreach (var path in new[] { "system virtual-switch", "system switch-interface" })
        {
            foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries))
            {
                Count(path, source: 1);
                if (_ifMap.TryGetValue(e.Key, out var t) && !string.IsNullOrEmpty(t)) Count(path, converted: 1);
                else Manual(path, e, RebuildCodes.InterfaceNotMappedUnused, e.Key);
            }
        }
    }

    /// <summary>Extra settings of an interface the plan creates: VLAN parent and ID, or the software switch type.</summary>
    private void CreatedInterfaceSets(string target, List<CliSet> sets)
    {
        var at = sets.FindIndex(s => s.Param == "vdom") + 1;
        if (_layout.Vlans.FirstOrDefault(v => v.Name == target) is { } vlan)
        {
            sets.Insert(at, new CliSet("interface", [vlan.Parent!], true));
            sets.Insert(at + 1, new CliSet("vlanid", [vlan.VlanId!.Value.ToString(CultureInfo.InvariantCulture)], false));
        }
        else if (_layout.Changes.Any(c => c.Name == target && !c.Existing && !c.Hardware))
        {
            sets.Insert(at, new CliSet("type", ["switch"], false));
        }
    }

    /// <summary>Created interfaces that no source interface is mapped to: written with only what makes them exist.</summary>
    private void EmitUnmappedCreated()
    {
        const string path = "system interface";
        foreach (var name in _layout.Created.Where(n => !_ifMap.ContainsValue(n)))
        {
            var entry = new CliEntry(name);
            entry.Sets.Add(new CliSet("vdom", ["root"], true));
            CreatedInterfaceSets(name, entry.Sets);
            if (entry.Sets.Count > 1) BlockIn("interfaces", path).Entries.Add(entry);
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
                if (t.Fatal is null && _layout.Created.Contains(target) && !t.Entry.Sets.Any(x => x.Param == "vdom")) t.Entry.Sets.Insert(0, new CliSet("vdom", ["root"], true));
                Addressing(e, target, t);
                if (t.Fatal is null && _layout.Created.Contains(target)) CreatedInterfaceSets(target, t.Entry.Sets);
                var section = string.Equals(target, _opt.ManagementInterface, StringComparison.Ordinal) ? "management"
                    : string.Equals(target, FactoryManagement, StringComparison.Ordinal) ? "factory-management" : "interfaces";
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
                    _readdressed.Add(target);
                    foreach (var p in new[] { "dns-server-override", "priority" })
                    {
                        if (e.Field(p) is { } f && _target.Find("system interface")!.Params.ContainsKey(p)) Put(p, f.Values);
                    }

                    LearnedRoute(e, Put);
                    break;
                case "pppoe":
                    Put("mode", ["pppoe"]);
                    _readdressed.Add(target);
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
                _readdressed.Add(target);
                break;
            case AddressingMode.Pppoe:
                Put("mode", ["pppoe"]);
                _readdressed.Add(target);
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
        if (gw != "enable")
        {
            _noDefaultRoute.Add(_ifMap.GetValueOrDefault(e.Key, e.Key));
            return;
        }
        var distance = e.Field("distance")?.Values;
        if (distance is null)
        {
            distance = ["1"];
            _applied.Add(new AppliedRule("D011", "system interface", e.Key, "distance"));
        }

        put("distance", distance, false);
    }

    /// <summary>
    /// The interface the PC is connected to on the factory configuration (static address with HTTPS or SSH access), when
    /// it is not the new management interface: its changes go after those of the management interface.
    /// </summary>
    private string? FactoryManagement => _factoryManagement ??= FindFactoryManagement() ?? "";

    private string? _factoryManagement;

    private string? FindFactoryManagement()
    {
        var e = (_req.Target.DefaultBackup?.GlobalScope.Block("system interface")?.Entries ?? [])
            .FirstOrDefault(x => x.Field("ip") is not null && (x.Field("allowaccess")?.Values ?? []).Any(a => a is "https" or "ssh"));
        return e is null || e.Key == _opt.ManagementInterface ? null : e.Key;
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

        foreach (var e in BlocksOf(doc, path).SelectMany(b => b.Entries).Where(e => e.Field("type")?.Values.FirstOrDefault() == "tunnel" && e.Key != "ssl.root" && !_known[Ref.Phase1].Contains(e.Key)))
        {
            if (BlocksOf(doc, "vpn ipsec phase1-interface").SelectMany(b => b.Entries).Any(p => p.Key == e.Key))
            {
                Count(path, source: 1);
                Manual(path, e, RebuildCodes.UnresolvedReference, "phase1-interface", e.Key);
            }
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

            var original = new List<string>();
            var applied = new List<string>();
            var lost = new List<string>();
            ValueHook hook = (param, values, t) =>
            {
                if (param == "utm-status") return null; // written below only if a profile is kept
                if (!ProfileParams.Contains(param)) return DefaultHook(path)!(param, values, t);
                var text = $"{param} {string.Join(' ', values)}";
                original.Add(text);
                if (ProfileTables.TryGetValue(param, out var table) && values.Count == 1
                    && (ExistsOnTarget(table, values[0]) || (_req.Target.DefaultBackup is null && PreloadedNames.GetValueOrDefault(param)?.Contains(values[0]) == true)))
                {
                    applied.Add(text);
                    return values;
                }

                if (_opt.Profiles == ProfileHandling.ApplyDefaults && DefaultProfile(param) is { } d)
                {
                    applied.Add($"{param} {d}");
                    _applied.Add(new AppliedRule("P001", path, e.Key, param));
                    return [d];
                }

                lost.Add(text);
                return null;
            };
            var t = Translate(path, [], e, node, e.Key, e.Key, hook);
            if (t.Fatal is null && t.Entry.Sets.Any(s => ProfileParams.Contains(s.Param) && s.Param is not ("profile-protocol-options" or "ssl-ssh-profile")))
            {
                // FortiOS lists utm-status before the profiles: written right before the first profile kept.
                var first = t.Entry.Sets.FindIndex(x => ProfileParams.Contains(x.Param));
                t.Entry.Sets.Insert(first, new CliSet("utm-status", ["enable"], false));
            }

            // No policy that had protection on the source leaves without it: what cannot be protected is disabled.
            var replaced = applied.Except(original).ToList();
            var disable = t.Fatal is null && (lost.Count > 0 || (_opt.Profiles == ProfileHandling.DisablePolicy && original.Count > 0 && replaced.Count > 0));
            if (disable && !t.Entry.Sets.Any(x => x.Param == "status" && x.Values.SequenceEqual(["disable"])))
            {
                t.Entry.Sets.RemoveAll(x => x.Param == "status");
                t.Entry.Sets.Insert(0, new CliSet("status", ["disable"], false));
            }

            if (original.Count > 0 && t.Fatal is null)
            {
                _profiles.Add(new PolicyProfiles(e.Key, original, applied, disable));
                var custom = original.Where(o => !applied.Contains(o)).ToList();
                if (custom.Count > 0)
                {
                    t.FieldItems.Add(Item(path, e.Key, RebuildCodes.ProfilesNotMigrated, [string.Join("; ", custom)], Mask(e.ToText()), _rules.Rules.First(r => r.Id == "E002")));
                }

                if (disable) _findings.Add(MigrationFinding.Warning(RebuildCodes.PolicyDisabledForProfiles, e.Key, string.Join("; ", lost.Count > 0 ? lost : custom)));
                else if (replaced.Count > 0) _findings.Add(MigrationFinding.Warning(RebuildCodes.DefaultProfilesApplied, e.Key, string.Join("; ", custom), string.Join("; ", replaced)));
            }

            if (Emit("policies", path, t, e.Key, e)) Count(path, rebuilt: 1);
            else Count(path, manual: 1);
        }
    }

    /// <summary>Policies towards a DHCP/PPPoE port that has no route: FortiOS picks the egress port from the routing table first.</summary>
    private void CheckPolicyRoutes()
    {
        var routed = _sections["routes"].Blocks.SelectMany(b => b.Entries).SelectMany(e => e.Sets).Where(x => x.Param == "device").SelectMany(x => x.Values).ToHashSet(StringComparer.Ordinal);
        foreach (var p in _sections["policies"].Blocks.SelectMany(b => b.Entries))
        {
            var dst = p.Sets.FirstOrDefault(x => x.Param == "dstintf")?.Values ?? [];
            foreach (var d in dst.Where(d => _noDefaultRoute.Contains(d) && !routed.Contains(d)))
            {
                _findings.Add(MigrationFinding.Warning(RebuildCodes.PolicyWithoutRoute, p.Key ?? "", d));
            }
        }
    }

    /// <summary>
    /// The preloaded profile of FortiOS 8.0 that replaces a custom profile of the source, when its name is documented
    /// (rule P001) and, with a factory-default backup, it exists there. Null when there is no documented equivalent.
    /// </summary>
    private string? DefaultProfile(string param)
    {
        if (!PreloadedProfiles.TryGetValue(param, out var name) || !ProfileTables.TryGetValue(param, out var table)) return null;
        return _req.Target.DefaultBackup is null || ExistsOnTarget(table, name) ? name : null;
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
            var section = intf == _opt.ManagementInterface ? "management" : intf == FactoryManagement ? "factory-management" : "dhcp";
            var block = section == "dhcp" ? BlockIn(section, path) : new CliBlock(path, true);
            block.Deletes.Add(id);
            if (section != "dhcp") _sections[section].Blocks.Insert(0, block);
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
            var section = intf is not null && (intf == _opt.ManagementInterface || intf == FactoryManagement) ? "management-dhcp" : "dhcp";
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

    /// <summary>DH groups that the Security Rating Insights consider insecure (Administration Guide 8.0, Phase 1 configuration).</summary>
    private static readonly HashSet<string> WeakDhGroups = new(StringComparer.Ordinal) { "1", "2", "5", "14", "22", "23", "24", "25", "26", "27" };

    /// <summary>Encryption and hash algorithms "not recommended" (Administration Guide 8.0, Phase 1 and Phase 2 configuration).</summary>
    private static readonly HashSet<string> WeakAlgorithms = new(StringComparer.Ordinal) { "null", "des", "3des", "md5", "sha1", "prfsha1" };

    /// <summary>
    /// IPsec tunnels written with IKEv1, weak DH groups or weak algorithms: kept as they are, because the other side
    /// must change at the same time, and reported with the best practices.
    /// </summary>
    private void WeakVpnFindings()
    {
        var phase2 = _sections["ipsec"].Blocks.Where(b => b.Path == "vpn ipsec phase2-interface").SelectMany(b => b.Entries).ToList();
        foreach (var p1 in _sections["ipsec"].Blocks.Where(b => b.Path == "vpn ipsec phase1-interface").SelectMany(b => b.Entries))
        {
            var weak = new List<string>();
            string[] Values(CliEntry e, string param) => [.. e.Sets.FirstOrDefault(x => x.Param == param)?.Values ?? []];
            void Check(CliEntry e, string label)
            {
                var dh = Values(e, "dhgrp").Where(WeakDhGroups.Contains).ToList();
                if (dh.Count > 0) weak.Add($"{label} DH {string.Join(" ", dh)}");
                var proposals = Values(e, "proposal").Where(p => p.Split('-').Any(WeakAlgorithms.Contains)).ToList();
                if (proposals.Count > 0) weak.Add($"{label} {string.Join(" ", proposals)}");
            }

            if (Values(p1, "ike-version").FirstOrDefault() == "1") weak.Add("IKEv1");
            Check(p1, "phase1");
            foreach (var p2 in phase2.Where(e => Values(e, "phase1name").FirstOrDefault() == p1.Key)) Check(p2, $"phase2 {p2.Key}");
            if (weak.Count > 0) _findings.Add(MigrationFinding.Warning(RebuildCodes.WeakVpn, p1.Key!, string.Join("; ", weak)));
        }
    }

    private void AddFindings(SslVpnInfo ssl)
    {
        WeakVpnFindings();
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
