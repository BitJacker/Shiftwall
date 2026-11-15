using System.Globalization;
using Shiftwall.Core.Config;

namespace Shiftwall.Features.Migration;

/// <summary>Outcome for one file of a batch.</summary>
public sealed record BatchItemResult(string SourcePath, MigrationRequest? Request, MigrationResult? Result, string? OutputPath, string? ReportPath, string? Error)
{
    public bool Succeeded => Error is null && Result is { IsValid: true };
}

/// <summary>
/// Migrates several sites that share the same source model, target and mapping template. In simulation mode
/// nothing is written.
/// </summary>
public static class BatchMigration
{
    public static IReadOnlyList<BatchItemResult> Run(
        IReadOnlyList<string> sourcePaths,
        TargetProfile target,
        MappingTemplate template,
        string outputDirectory,
        ReportContext reportTemplate,
        bool simulate,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var results = new List<BatchItemResult>(sourcePaths.Count);
        for (var i = 0; i < sourcePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(RunOne(sourcePaths[i], sourcePaths, target, template, outputDirectory, reportTemplate, simulate));
            progress?.Report((i + 1, sourcePaths.Count));
        }

        return results;
    }

    private static BatchItemResult RunOne(string path, IReadOnlyList<string> allSources, TargetProfile target, MappingTemplate template, string outDir, ReportContext ctx, bool simulate)
    {
        try
        {
            var inspection = ConfigInspector.Inspect(File.ReadAllBytes(path));
            if (inspection.Kind != ConfigFileKind.FortiOsText)
            {
                return new BatchItemResult(path, null, null, null, null, MigrationText.Format(MigrationCodes.SourceNotFortiOsText, [], null, ctx.Culture));
            }

            var source = SourceProfile.From(inspection);
            var mapping = InterfaceMapping.FromTemplate(template, source.PhysicalInterfaces.Select(p => p.Name));
            var request = new MigrationRequest(source, target, mapping);
            var result = MigrationEngine.Run(request);
            if (simulate || !result.IsValid || result.OutputBytes is null)
            {
                return new BatchItemResult(path, request, result, null, null, null);
            }

            var label = target.ModelId ?? target.PlatformCode;
            var outPath = SafeOutput.SuggestName(path, label, outDir);
            SafeOutput.Write(outPath, result.OutputBytes, allSources);
            var reportPath = Path.ChangeExtension(outPath, ".report.html");
            var html = HtmlReport.Build(request, result, ctx with { SourceName = Path.GetFileName(path), TargetName = Path.GetFileName(outPath) });
            SafeOutput.Write(reportPath, System.Text.Encoding.UTF8.GetBytes(html), allSources);
            return new BatchItemResult(path, request, result, outPath, reportPath, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Message only (file system problem); never config content.
            return new BatchItemResult(path, null, null, null, null, ex.Message);
        }
    }

    /// <summary>One-line summary per file for the batch table.</summary>
    public static string Summary(BatchItemResult item, CultureInfo culture)
    {
        if (item.Error is not null) return item.Error;
        if (item.Result is null) return "";
        var errors = item.Result.Analysis.Blocking.Concat(item.Result.Validation.Where(v => v.Severity == FindingSeverity.Error)).ToList();
        return errors.Count == 0
            ? MigrationText.Format(MigrationCodes.OutputValid, [], null, culture)
            : string.Join(" ", errors.Select(e => e.Format(culture)));
    }
}
