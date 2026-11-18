using System.Globalization;
using System.Text.RegularExpressions;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Migration;

public sealed record MigrationOptions(bool RemoveDiskSectionsWhenTargetHasNoDisk = true);

public sealed record MigrationRequest(SourceProfile Source, TargetProfile Target, InterfaceMapping Mapping, MigrationOptions? Options = null)
{
    public MigrationOptions EffectiveOptions => Options ?? new MigrationOptions();
}

/// <summary>Pre-checks and mapping analysis, computed before anything is transformed.</summary>
public sealed record MigrationAnalysis(
    IReadOnlyList<MigrationFinding> PreChecks,
    IReadOnlyList<MigrationFinding> MappingFindings,
    IReadOnlyList<InterfaceReference> References,
    IReadOnlyDictionary<string, int> UsageCount,
    IReadOnlyList<string> UnusedUnmapped)
{
    public bool IsBlocked => PreChecks.Concat(MappingFindings).Any(f => f.Severity == FindingSeverity.Error);

    public IEnumerable<MigrationFinding> Blocking => PreChecks.Concat(MappingFindings).Where(f => f.Severity == FindingSeverity.Error);
}

/// <summary>The outcome of a migration run. Nothing is written to disk by the engine.</summary>
public sealed record MigrationResult(
    MigrationAnalysis Analysis,
    string SourceText,
    string? OutputText,
    byte[]? OutputBytes,
    IReadOnlyList<MigrationFinding> Changes,
    IReadOnlyList<MigrationFinding> Validation,
    IReadOnlyList<MigrationFinding> Checklist)
{
    public bool IsBlocked => Analysis.IsBlocked;

    /// <summary>True when an output was produced and passed every validation.</summary>
    public bool IsValid => OutputText is not null && Validation.All(v => v.Severity != FindingSeverity.Error);
}

/// <summary>
/// Migrates a FortiOS configuration to another FortiGate model: header, interface renaming (exact tokens in
/// documented interface fields only), removal of unused unmapped ports and, when the target has no disk, of the
/// disk sections. Syntax is never translated between FortiOS versions. ENC values are never touched.
/// </summary>
public static partial class MigrationEngine
{
    private const string LogDiskBlock = "log disk setting";
    private const string StorageBlock = "system storage";

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex BareToken();

    public static MigrationAnalysis Analyze(MigrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pre = new List<MigrationFinding>();
        var mapping = new List<MigrationFinding>();
        var source = request.Source;
        var target = request.Target;
        var inspection = source.Inspection;

        // --- Source state ---
        if (inspection.HasErrors)
        {
            pre.Add(MigrationFinding.Error(MigrationCodes.SourceHasErrors));
        }
        else if (inspection.IsPartial)
        {
            pre.Add(MigrationFinding.Warning(MigrationCodes.SourcePartial));
        }

        if (inspection.PrivateDataEncryption) pre.Add(MigrationFinding.Warning(MigrationCodes.PrivateDataEncryption));
        if (inspection.MaskedPasswordCount > 0) pre.Add(MigrationFinding.Warning(MigrationCodes.MaskedPasswords, inspection.MaskedPasswordCount.ToString(CultureInfo.InvariantCulture)));
        if (inspection.IsMultiVdom) pre.Add(MigrationFinding.Info(MigrationCodes.MultiVdom, string.Join(", ", inspection.Vdoms)));

        // --- Firmware: never translate syntax between versions ---
        if (source.Version is null || target.Version is null)
        {
            pre.Add(MigrationFinding.Warning(MigrationCodes.FirmwareUnknown, source.Version ?? "?", target.Version ?? "?"));
        }
        else if (source.Version != target.Version)
        {
            pre.Add(MigrationFinding.Warning(MigrationCodes.FirmwareMismatch, source.Version, target.Version));
        }

        // --- Target data quality ---
        if (target.HeaderLines.Count == 0) pre.Add(MigrationFinding.Warning(MigrationCodes.HeaderNotAvailable));
        if (!target.InterfacesVerified || target.PhysicalInterfaces.Count == 0)
        {
            pre.Add(MigrationFinding.Error(MigrationCodes.TargetInterfacesUnverified, target.ModelId ?? "?"));
        }
        else if (target.Source == TargetSource.Database && target.InterfacesNote is { Length: > 0 } note)
        {
            pre.Add(MigrationFinding.Info(MigrationCodes.TargetInterfacesNote, note));
        }

        var hasDiskSections = HasDiskSections(source.Document);
        if (target.HasDisk is null || !target.HasDiskVerified)
        {
            if (hasDiskSections) pre.Add(MigrationFinding.Warning(MigrationCodes.DiskUnknown));
        }
        else if (target.HasDisk == false && hasDiskSections && request.EffectiveOptions.RemoveDiskSectionsWhenTargetHasNoDisk)
        {
            pre.Add(MigrationFinding.Info(MigrationCodes.DiskSectionsWillBeRemoved));
        }

        if (source.VirtualSwitches.Count > 0 || target.VirtualSwitches.Count > 0)
        {
            pre.Add(MigrationFinding.Warning(MigrationCodes.VirtualSwitchReview,
                string.Join(", ", source.VirtualSwitches.Select(s => s.Name)),
                string.Join(", ", target.VirtualSwitches.Select(s => s.Name))));
        }

        // --- Mapping ---
        var sourceNames = source.PhysicalInterfaces.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var targetNames = target.PhysicalInterfaces.ToHashSet(StringComparer.Ordinal);
        var references = InterfaceReferenceFinder.Find(source.Document, sourceNames);
        var usage = sourceNames.ToDictionary(n => n, n => references.Count(r => r.Interface == n && r.Kind is ReferenceKind.Field or ReferenceKind.Key), StringComparer.Ordinal);
        var unusedUnmapped = new List<string>();

        foreach (var iface in source.PhysicalInterfaces.Select(i => i.Name))
        {
            var mapped = request.Mapping[iface];
            if (mapped is null)
            {
                if (usage[iface] > 0)
                {
                    mapping.Add(MigrationFinding.Error(MigrationCodes.UnmappedUsed, iface, usage[iface].ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    unusedUnmapped.Add(iface);
                    mapping.Add(MigrationFinding.Info(MigrationCodes.UnmappedUnused, iface));
                }
            }
            else if (targetNames.Count > 0 && !targetNames.Contains(mapped))
            {
                mapping.Add(MigrationFinding.Error(MigrationCodes.MappedToUnknownTarget, iface, mapped));
            }
            else if (InterfaceMapping.RoleOf(iface) == InterfaceMapping.PortRole.SwitchModeLan && InterfaceMapping.RoleOf(mapped) != InterfaceMapping.PortRole.SwitchModeLan)
            {
                mapping.Add(MigrationFinding.Warning(MigrationCodes.SwitchModePortToSinglePort, iface, mapped));
            }
        }

        foreach (var dup in request.Mapping.DuplicateTargets())
        {
            mapping.Add(MigrationFinding.Error(MigrationCodes.DuplicateTarget, dup));
        }

        var mappedTargets = request.Mapping.Entries.Values.Where(v => v is not null).ToHashSet(StringComparer.Ordinal);
        var targetOnly = target.PhysicalInterfaces.Where(t => !mappedTargets.Contains(t)).ToList();
        if (targetOnly.Count > 0)
        {
            mapping.Add(MigrationFinding.Info(MigrationCodes.TargetOnlyPorts, string.Join(", ", targetOnly)));
        }

        foreach (var p in references.Where(r => r.Kind == ReferenceKind.Possible && request.Mapping[r.Interface] != r.Interface))
        {
            mapping.Add(new MigrationFinding(MigrationCodes.PossibleReference, FindingSeverity.Warning, [p.Interface, p.Path], p.Line));
        }

        return new MigrationAnalysis(pre, mapping, references, usage, unusedUnmapped);
    }

    public static MigrationResult Run(MigrationRequest request)
    {
        var analysis = Analyze(request);
        var sourceText = request.Source.Inspection.Text.Text;
        var checklist = BuildChecklist(request, analysis);
        if (analysis.IsBlocked)
        {
            return new MigrationResult(analysis, sourceText, null, null, [], [], checklist);
        }

        var changes = new List<MigrationFinding>();
        var doc = ConfigParser.Parse(sourceText); // fresh, mutable copy
        var renames = request.Mapping.Entries
            .Where(kv => kv.Value is not null && kv.Value != kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);

        // 1. Rename interfaces (single pass: swaps such as wan1<->wan2 are safe).
        var counts = RenameInterfaces(doc, renames);
        foreach (var (from, to) in renames)
        {
            changes.Add(MigrationFinding.Info(MigrationCodes.Renamed, from, to, counts.GetValueOrDefault(from).ToString(CultureInfo.InvariantCulture)));
        }

        // 2. Remove definitions of unused, unmapped physical ports (they do not exist on the target).
        foreach (var name in analysis.UnusedUnmapped)
        {
            if (RemoveDefinition(doc, name)) changes.Add(MigrationFinding.Info(MigrationCodes.DefinitionRemoved, name));
        }

        // 3. Disk sections when the target has (verifiably) no disk.
        if (request.Target is { HasDisk: false, HasDiskVerified: true } && request.EffectiveOptions.RemoveDiskSectionsWhenTargetHasNoDisk)
        {
            foreach (var path in new[] { StorageBlock, LogDiskBlock })
            {
                foreach (var block in DiskBlocks(doc, path))
                {
                    block.Parent!.Remove(block);
                    changes.Add(MigrationFinding.Info(MigrationCodes.SectionRemoved, path));
                }
            }
        }

        // 4. Header.
        if (request.Target.HeaderLines.Count > 0)
        {
            if (ReplaceConfigVersion(doc, request.Target.HeaderLines))
            {
                changes.Add(MigrationFinding.Info(MigrationCodes.HeaderReplaced, request.Target.PlatformCode ?? "?", request.Target.Version ?? "?"));
            }
            else
            {
                changes.Add(MigrationFinding.Warning(MigrationCodes.HeaderNotReplaced));
                if (!checklist.Any(c => c.Code == MigrationCodes.CheckHeader)) checklist.Add(MigrationFinding.Info(MigrationCodes.CheckHeader));
            }
        }

        var output = doc.ToText(sourceText.Length);
        var bytes = request.Source.Inspection.Text.Encode(output);
        var validation = Validate(request, sourceText, output);
        return new MigrationResult(analysis, sourceText, output, bytes, changes, validation, checklist);
    }

    private static Dictionary<string, int> RenameInterfaces(ConfigDocument doc, IReadOnlyDictionary<string, string> renames)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (renames.Count == 0) return counts;

        var keyEdits = new List<EditEntry>();
        var fieldEdits = new List<(FieldNode Node, List<int> Indexes)>();
        InterfaceReferenceFinder.Walk(doc, [], [], null, (node, chain, _, _) =>
        {
            switch (node)
            {
                case EditEntry entry when chain.Count > 0 && InterfaceReferenceRules.IsInterfaceKeyedBlock(chain) && renames.ContainsKey(entry.Key):
                    keyEdits.Add(entry);
                    break;
                case FieldNode field when InterfaceReferenceRules.IsInterfaceField(chain, field.Name):
                    var idx = InterfaceReferenceFinder.ValueTokens(field).Where(t => renames.ContainsKey(t.Token.Value)).Select(t => t.Index).ToList();
                    if (idx.Count > 0) fieldEdits.Add((field, idx));
                    break;
            }
        });

        foreach (var entry in keyEdits)
        {
            var token = entry.Statement.Tokens[1];
            var to = renames[token.Value];
            entry.SetStatement(entry.Statement.WithTokenReplaced(1, RenderLike(token, to)));
            counts[token.Value] = counts.GetValueOrDefault(token.Value) + 1;
        }

        foreach (var (field, indexes) in fieldEdits)
        {
            var statement = field.Statement;
            foreach (var i in indexes.OrderByDescending(i => i))
            {
                var token = statement.Tokens[i];
                counts[token.Value] = counts.GetValueOrDefault(token.Value) + 1;
                statement = statement.WithTokenReplaced(i, RenderLike(token, renames[token.Value]));
            }

            field.Parent!.Replace(field, new FieldNode(statement));
        }

        return counts;
    }

    /// <summary>Writes <paramref name="value"/> with the same quoting style as <paramref name="original"/>.</summary>
    private static string RenderLike(ConfigToken original, string value) => original.Quote switch
    {
        '"' => ConfigToken.Render(value, '"'),
        '\'' => ConfigToken.Render(value, '\''),
        _ when BareToken().IsMatch(value) => value,
        _ => ConfigToken.Render(value),
    };

    private static bool RemoveDefinition(ConfigDocument doc, string name)
    {
        var block = doc.GlobalScope.Block("system interface");
        var entry = block?.Entries.FirstOrDefault(e => e.Key == name);
        return entry is not null && block!.Remove(entry);
    }

    private static bool HasDiskSections(ConfigDocument doc) =>
        DiskBlocks(doc, StorageBlock).Any() || DiskBlocks(doc, LogDiskBlock).Any();

    private static IEnumerable<ConfigBlock> DiskBlocks(ConfigDocument doc, string path) =>
        doc.GlobalScope.Blocks.Where(b => b.Path == path).ToList();

    /// <summary>
    /// Replaces the "config-version section" of the first header line with the target's, as in Fortinet's manual
    /// migration procedure (FortiOS 8.0.0 Best Practices, "Migrating a configuration manually", step 4: only
    /// <c>FGT80F-7.0.6-FW-build0366-220606</c> changes, <c>:opmode=0:vdom=0:user=admin</c> stays). The rest of the line
    /// (operation mode, VDOM flag, user) and the other header lines describe the source configuration and are kept.
    /// Returns false when either side has no recognisable config-version line.
    /// </summary>
    internal static bool ReplaceConfigVersion(ConfigDocument doc, IReadOnlyList<Statement> targetHeader)
    {
        var targetSection = targetHeader.Select(l => ConfigVersionSection().Match(l.Raw)).FirstOrDefault(m => m.Success)?.Groups["section"].Value;
        if (string.IsNullOrEmpty(targetSection)) return false;
        foreach (var line in doc.Header.Lines)
        {
            var m = ConfigVersionSection().Match(line.Raw);
            if (!m.Success) continue;
            var node = doc.Children.OfType<TriviaNode>().FirstOrDefault(t => ReferenceEquals(t.Statement, line));
            if (node is null) return false;
            var g = m.Groups["section"];
            var raw = string.Concat(line.Raw.AsSpan(0, g.Index), targetSection, line.Raw.AsSpan(g.Index + g.Length));
            doc.Replace(node, TriviaNode.FromText(raw));
            return true;
        }

        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*#config-version=(?<section>[^:\r\n]+)")]
    private static partial System.Text.RegularExpressions.Regex ConfigVersionSection();

    private static List<MigrationFinding> Validate(MigrationRequest request, string sourceText, string output)
    {
        var findings = new List<MigrationFinding>();
        var outDoc = ConfigParser.Parse(output);

        foreach (var d in outDoc.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Code is DiagnosticCodes.UnexpectedEnd or DiagnosticCodes.UnexpectedNext))
        {
            findings.Add(new MigrationFinding(MigrationCodes.OutputStructure, FindingSeverity.Error, [d.Code], d.Line));
        }

        var targetNames = request.Target.PhysicalInterfaces.ToHashSet(StringComparer.Ordinal);
        foreach (var p in InterfaceInventory.Physical(outDoc).Where(p => !targetNames.Contains(p.Name)))
        {
            findings.Add(new MigrationFinding(MigrationCodes.PhysicalNotOnTarget, FindingSeverity.Error, [p.Name], p.Line));
        }

        // Names that existed on the source but not on the target must not be referenced anymore.
        var gone = request.Source.PhysicalInterfaces.Select(i => i.Name).Where(n => !targetNames.Contains(n)).ToHashSet(StringComparer.Ordinal);
        foreach (var r in InterfaceReferenceFinder.Find(outDoc, gone).Where(r => r.Kind != ReferenceKind.Possible))
        {
            findings.Add(new MigrationFinding(MigrationCodes.LeftoverReference, FindingSeverity.Error, [r.Interface, r.Path], r.Line));
        }

        foreach (var sw in InterfaceInventory.VirtualSwitches(outDoc))
        {
            foreach (var m in sw.Members.Where(m => !targetNames.Contains(m)))
            {
                findings.Add(new MigrationFinding(MigrationCodes.SwitchMemberNotOnTarget, FindingSeverity.Error, [sw.Name, m], sw.Line));
            }
        }

        // Every ENC value in the output must exist, byte for byte, in the source.
        var sourceEnc = EncValues(ConfigParser.Parse(sourceText)).ToHashSet(StringComparer.Ordinal);
        if (EncValues(outDoc).Any(v => !sourceEnc.Contains(v)))
        {
            findings.Add(MigrationFinding.Error(MigrationCodes.EncryptedValueChanged));
        }

        if (findings.Count == 0) findings.Add(MigrationFinding.Info(MigrationCodes.OutputValid));
        return findings;
    }

    private static IEnumerable<string> EncValues(ConfigDocument doc) =>
        doc.Descendants().OfType<FieldNode>().SelectMany(f =>
        {
            var t = f.Statement.Tokens;
            return Enumerable.Range(2, Math.Max(0, t.Count - 3)).Where(i => !t[i].IsQuoted && t[i].Value == "ENC").Select(i => t[i + 1].Raw);
        });

    private static List<MigrationFinding> BuildChecklist(MigrationRequest request, MigrationAnalysis analysis)
    {
        var list = new List<MigrationFinding> { MigrationFinding.Info(MigrationCodes.CheckLab) };
        var codes = analysis.PreChecks.Concat(analysis.MappingFindings).Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
        if (codes.Contains(MigrationCodes.FirmwareMismatch) || codes.Contains(MigrationCodes.FirmwareUnknown)) list.Add(MigrationFinding.Info(MigrationCodes.CheckFirmware));
        if (request.Source.Inspection.EncryptedValueCount > 0 || codes.Contains(MigrationCodes.PrivateDataEncryption)) list.Add(MigrationFinding.Info(MigrationCodes.CheckSecrets));
        if (codes.Contains(MigrationCodes.HeaderNotAvailable)) list.Add(MigrationFinding.Info(MigrationCodes.CheckHeader));
        if (codes.Contains(MigrationCodes.DiskSectionsWillBeRemoved) || codes.Contains(MigrationCodes.DiskUnknown)) list.Add(MigrationFinding.Info(MigrationCodes.CheckLogging));
        if (codes.Contains(MigrationCodes.PossibleReference)) list.Add(MigrationFinding.Info(MigrationCodes.CheckPossibleReferences));
        if (codes.Contains(MigrationCodes.TargetOnlyPorts)) list.Add(MigrationFinding.Info(MigrationCodes.CheckTargetOnlyPorts));
        if (codes.Contains(MigrationCodes.VirtualSwitchReview)) list.Add(MigrationFinding.Info(MigrationCodes.CheckSwitch));
        if (codes.Contains(MigrationCodes.TargetInterfacesNote) || request.Target.Source == TargetSource.Database) list.Add(MigrationFinding.Info(MigrationCodes.CheckHaMgmt));
        list.Add(MigrationFinding.Info(MigrationCodes.CheckLicensing));
        list.Add(MigrationFinding.Info(MigrationCodes.CheckManagementAccess));
        return list;
    }
}
