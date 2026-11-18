using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Tests.Migration;

public sealed class MigrationRulesTests
{
    private static MigrationResult Run(string sourceText, TargetProfile target, Action<InterfaceMapping>? edit = null)
    {
        var source = Fixtures.Source(sourceText);
        var mapping = InterfaceMapping.Auto(source.PhysicalInterfaces.Select(p => p.Name), target.PhysicalInterfaces);
        edit?.Invoke(mapping);
        return MigrationEngine.Run(new MigrationRequest(source, target, mapping));
    }

    [Fact]
    public void Rename_TouchesExactTokensOnly()
    {
        var text = Fixtures.SourceText(["port1", "port10"], """
            config firewall policy
                edit 1
                    set srcintf "port1" "port10"
                    set dstintf "port10"
                    set name "port1"
                    set comments "uplink port1 and port10"
                next
            end

            """);
        var result = Run(text, Fixtures.Target("x1", "port10"), m => m["port1"] = "x1");

        Assert.True(result.IsValid);
        Assert.Contains("set srcintf \"x1\" \"port10\"", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("set dstintf \"port10\"", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("set name \"port1\"", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("set comments \"uplink port1 and port10\"", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("edit \"x1\"", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void Swap_IsAppliedInOnePass()
    {
        var text = Fixtures.SourceText(["wan1", "wan2"], """
            config router static
                edit 1
                    set device "wan1"
                next
                edit 2
                    set device "wan2"
                next
            end

            """);
        var result = Run(text, Fixtures.Target("wan1", "wan2"), m => { m["wan1"] = "wan2"; m["wan2"] = "wan1"; });
        var doc = ConfigParser.Parse(result.OutputText!);
        Assert.Equal(["wan2", "wan1"], doc.Block("router static")!.Entries.Select(e => e.Field("device")!.Values[0]));
        Assert.Equal(["wan2", "wan1"], doc.Block("system interface")!.Entries.Select(e => e.Key));
    }

    [Fact]
    public void EncValues_AreNeverTouched_EvenWhenTheyLookLikeInterfaces()
    {
        var text = Fixtures.SourceText(["port1"], """
            config system interface
                edit "pppoe-x"
                    set interface "port1"
                    set password ENC port1
                next
            end

            """);
        var result = Run(text, Fixtures.Target("wan"), m => m["port1"] = "wan");
        Assert.True(result.IsValid);
        Assert.Contains("set password ENC port1", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("set interface \"wan\"", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void UsedButUnmapped_BlocksAndProducesNoOutput()
    {
        var text = Fixtures.SourceText(["dmz"], """
            config firewall policy
                edit 1
                    set srcintf "dmz"
                    set dstintf "dmz"
                next
            end

            """);
        var result = Run(text, Fixtures.Target("port1"));
        Assert.True(result.IsBlocked);
        Assert.Null(result.OutputText);
        Assert.Contains(result.Analysis.MappingFindings, f => f.Code == MigrationCodes.UnmappedUsed && f.Arguments.SequenceEqual(["dmz", "2"]));
    }

    [Fact]
    public void MappingToMissingOrDuplicateTargets_Blocks()
    {
        var text = Fixtures.SourceText(["a1", "a2"], "config router static\n    edit 1\n        set device \"a1\"\n    next\n    edit 2\n        set device \"a2\"\n    next\nend\n");
        var missing = Run(text, Fixtures.Target("p1", "p2"), m => { m["a1"] = "p1"; m["a2"] = "nope"; });
        Assert.Contains(missing.Analysis.MappingFindings, f => f.Code == MigrationCodes.MappedToUnknownTarget);
        var dup = Run(text, Fixtures.Target("p1", "p2"), m => { m["a1"] = "p1"; m["a2"] = "p1"; });
        Assert.Contains(dup.Analysis.MappingFindings, f => f.Code == MigrationCodes.DuplicateTarget && f.Arguments[0] == "p1");
        Assert.True(dup.IsBlocked);
    }

    [Fact]
    public void FirmwareMismatch_IsReported_AndSyntaxIsNotTranslated()
    {
        var text = Fixtures.SourceText(["port1"], "", version: "7.2.12");
        var result = Run(text, Fixtures.Target("port1"));
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.FirmwareMismatch && f.Arguments.SequenceEqual(["7.2.12", "7.4.13"]));
        Assert.Contains(result.Checklist, c => c.Code == MigrationCodes.CheckFirmware);
    }

    [Fact]
    public void DiskSections_AreKept_WhenTargetDiskIsUnknown()
    {
        var text = Fixtures.SourceText(["port1"], "config log disk setting\n    set status enable\nend\n");
        var result = Run(text, Fixtures.Target("port1")); // no database model: disk unknown
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.DiskUnknown);
        Assert.Contains("config log disk setting", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetFromDatabase_WithoutVerifiedInterfaces_Blocks()
    {
        var target = TargetProfile.FromDatabase(Fixtures.Db.FindModel("FG-60E")!, "7.4.13");
        var result = Run(Fixtures.SourceText(["port1"], ""), target);
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.TargetInterfacesUnverified);
        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void TargetFromDatabase_KeepsSourceHeader_AndAsksToReplaceIt()
    {
        var target = TargetProfile.FromDatabase(Fixtures.Db.FindModel("FG-60F")!, "7.4.13");
        var result = Run(Fixtures.SourceText(["wan1"], ""), target);
        Assert.True(result.IsValid);
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.HeaderNotAvailable);
        Assert.Contains(result.Checklist, c => c.Code == MigrationCodes.CheckHeader);
        Assert.StartsWith("#config-version=FGTSRC-", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void CrLfSource_StaysCrLf_IncludingTheNewHeader()
    {
        var text = Fixtures.SourceText(["port1"], "").Replace("\n", "\r\n", StringComparison.Ordinal);
        var result = Run(text, Fixtures.Target("port1"));
        Assert.True(result.IsValid);
        Assert.DoesNotContain("\n", result.OutputText!.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.StartsWith("#config-version=FGTSYN-", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiVdom_RenamesInEveryVdom()
    {
        const string text = """
            #config-version=FGTSRC-7.4.13-FW-build2933-261022:opmode=0:vdom=1:user=admin
            #buildno=2933

            config vdom
            edit root
            next
            edit branch
            next
            end

            config global
            config system global
                set hostname "mv"
            end
            config system interface
                edit "port1"
                    set vdom "root"
                    set type physical
                next
                edit "port2"
                    set vdom "branch"
                    set type physical
                next
            end
            end

            config vdom
            edit root
            config router static
                edit 1
                    set device "port1"
                next
            end
            next
            edit branch
            config router static
                edit 1
                    set device "port2"
                next
            end
            next
            end

            """;
        var result = Run(text, Fixtures.Target("wan1", "wan2"), m => { m["port1"] = "wan1"; m["port2"] = "wan2"; });
        Assert.True(result.IsValid);
        Assert.Contains(result.Analysis.PreChecks, f => f.Code == MigrationCodes.MultiVdom);
        Assert.Contains("set device \"wan1\"", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("set device \"wan2\"", result.OutputText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"port1\"", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_DetectsLeftoverPhysicalInterfaces()
    {
        // A physical port that is used only through an undocumented field is "unused" for the engine:
        // its definition is removed and the possible reference is reported, never silently renamed.
        var text = Fixtures.SourceText(["port7"], "config system snmp sysinfo\n    set trap-source-intf \"port7\"\nend\n");
        var result = Run(text, Fixtures.Target("port1"));
        Assert.True(result.IsValid);
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.DefinitionRemoved && c.Arguments[0] == "port7");
        Assert.Contains(result.Analysis.MappingFindings, f => f.Code == MigrationCodes.PossibleReference);
    }

    [Fact]
    public void AutoMapping_MatchesEqualNamesOnly()
    {
        var m = InterfaceMapping.Auto(["wan1", "internal1", "a"], ["wan1", "port1", "a"]);
        Assert.Equal("wan1", m["wan1"]);
        Assert.Null(m["internal1"]);
        Assert.Equal("a", m["a"]);
    }

    [Fact]
    public void Template_RoundTripsThroughJson()
    {
        var m = new InterfaceMapping { ["internal1"] = "port1", ["wan1"] = "wan1", ["dmz"] = null };
        var json = m.ToTemplate("t", "FGT60F", "FG-70G").ToJson();
        var back = InterfaceMapping.FromTemplate(MappingTemplate.FromJson(json), ["internal1", "wan1", "dmz", "other"]);
        Assert.Equal("port1", back["internal1"]);
        Assert.Equal("wan1", back["wan1"]);
        Assert.Null(back["dmz"]);
        Assert.Null(back["other"]);
        Assert.DoesNotContain("dmz", json, StringComparison.Ordinal);
    }

    /// <summary>FortiOS Best Practices, "Migrating a configuration manually", step 4: only the config-version section changes.</summary>
    [Fact]
    public void Header_OnlyConfigVersionSectionIsReplaced()
    {
        var text = "#config-version=FGT60F-7.4.13-FW-build2933-261022:opmode=0:vdom=1:user=ops\n#conf_file_ver=42\n#buildno=2933\n#global_vdom=1\n" +
                   "config system interface\n    edit \"port1\"\n        set type physical\n    next\nend\n";
        var source = Fixtures.Source(text);
        var target = Fixtures.Target("port1");
        var result = MigrationEngine.Run(new MigrationRequest(source, target, InterfaceMapping.Auto(source.PhysicalInterfaces.Select(p => p.Name), target.PhysicalInterfaces)));
        var lines = result.OutputText!.Split('\n');
        Assert.Equal("#config-version=FGTSYN-7.4.13-FW-build2933-261022:opmode=0:vdom=1:user=ops", lines[0]);
        Assert.Equal(["#conf_file_ver=42", "#buildno=2933", "#global_vdom=1"], lines[1..4]);
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.HeaderReplaced);
    }

    [Fact]
    public void Header_WithoutConfigVersionLine_IsLeftAndFlagged()
    {
        var text = "config system interface\n    edit \"port1\"\n        set type physical\n    next\nend\n";
        var source = Fixtures.Source(text);
        var target = Fixtures.Target("port1");
        var result = MigrationEngine.Run(new MigrationRequest(source, target, InterfaceMapping.Auto(source.PhysicalInterfaces.Select(p => p.Name), target.PhysicalInterfaces)));
        Assert.NotNull(result.OutputText);
        Assert.StartsWith("config system interface", result.OutputText);
        Assert.Contains(result.Changes, c => c.Code == MigrationCodes.HeaderNotReplaced);
        Assert.Contains(result.Checklist, c => c.Code == MigrationCodes.CheckHeader);
    }
}
