using System.Text.RegularExpressions;

namespace Shiftwall.Core.Data;

/// <summary>
/// Enforces the "no invented data" rule: every verified datum must cite an official Fortinet source
/// with a verification date; every unverified datum must explain why and carry no value.
/// </summary>
public static partial class DatabaseValidator
{
    /// <summary>Hosts accepted as official Fortinet sources.</summary>
    public static readonly IReadOnlyList<string> OfficialHosts =
    [
        "docs.fortinet.com",
        "www.fortinet.com",
        "community.fortinet.com",
        // Attachments (PDF quick start guides) linked from docs.fortinet.com pages.
        "fortinetweb.s3.amazonaws.com",
    ];

    private static readonly DateOnly MinVerifiedOn = new(2020, 1, 1);

    [GeneratedRegex(@"^(FG|FWF|FGR|FFW|FOS)-[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex ModelIdRegex();

    // FortiOS interface names: lowercase, max 15 characters.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,14}$")]
    private static partial Regex InterfaceNameRegex();

    [GeneratedRegex(@"^[0-9]{1,5}$")]
    private static partial Regex BuildRegex();

    public static IReadOnlyList<string> Validate(ModelsDocument models, ReleasesDocument releases, UpgradePathsDocument paths, DateOnly? today = null)
    {
        var errors = new List<string>();
        var now = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        ValidateModels(models, errors, now);
        ValidateReleases(releases, errors, now);
        ValidateUpgradePaths(paths.UpgradePaths, errors, now);
        return errors;
    }

    private static void ValidateModels(ModelsDocument doc, List<string> errors, DateOnly now)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in doc.Models)
        {
            var where = $"models.json [{m.Id}]";
            if (!ModelIdRegex().IsMatch(m.Id)) errors.Add($"{where}: invalid model id.");
            if (!ids.Add(m.Id)) errors.Add($"{where}: duplicate model id.");
            foreach (var alias in m.Aliases ?? [])
            {
                if (!ModelIdRegex().IsMatch(alias)) errors.Add($"{where}: invalid alias '{alias}'.");
                if (!ids.Add(alias)) errors.Add($"{where}: alias '{alias}' duplicates another id.");
            }

            if (string.IsNullOrWhiteSpace(m.DisplayName)) errors.Add($"{where}: display_name is empty.");
            if (m.Platform is not ("hardware" or "vm")) errors.Add($"{where}: platform must be 'hardware' or 'vm'.");

            CheckSourced(m.Interfaces, $"{where}.interfaces", errors, now);
            CheckSourced(m.HasDisk, $"{where}.has_disk", errors, now);
            CheckSourced(m.DefaultSwitches, $"{where}.default_switches", errors, now);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in m.Interfaces.Value ?? [])
            {
                if (!InterfaceNameRegex().IsMatch(i.Name)) errors.Add($"{where}: invalid interface name '{i.Name}'.");
                if (!names.Add(i.Name)) errors.Add($"{where}: duplicate interface '{i.Name}'.");
                if (string.IsNullOrWhiteSpace(i.Media)) errors.Add($"{where}: interface '{i.Name}' has no media.");
            }

            if (m.Interfaces.IsVerified && names.Count == 0) errors.Add($"{where}: verified interface list is empty.");

            foreach (var sw in m.DefaultSwitches.Value ?? [])
            {
                foreach (var member in sw.Members.Where(x => !names.Contains(x)))
                {
                    errors.Add($"{where}: switch '{sw.Name}' member '{member}' is not a listed interface.");
                }
            }
        }
    }

    private static void ValidateReleases(ReleasesDocument doc, List<string> errors, DateOnly now)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in doc.Releases)
        {
            var where = $"fortios-releases.json [{r.Version}]";
            if (!FortiOsVersion.TryParse(r.Version, out _)) errors.Add($"{where}: invalid version.");
            if (!seen.Add(r.Version)) errors.Add($"{where}: duplicate version.");

            CheckSourced(r.Build, $"{where}.build", errors, now);
            CheckSourced(r.ReleaseDate, $"{where}.release_date", errors, now);
            CheckSourced(r.SupportedModels, $"{where}.supported_models", errors, now);

            if (r.Build.Value is { } b && !BuildRegex().IsMatch(b)) errors.Add($"{where}: invalid build '{b}'.");
            if (r.ReleaseDate.Value is { } d && (d < new DateOnly(2000, 1, 1) || d > now)) errors.Add($"{where}: release_date {d:yyyy-MM-dd} out of range.");
            foreach (var id in (r.SupportedModels.Value ?? []).Where(id => !ModelIdRegex().IsMatch(id)))
            {
                errors.Add($"{where}: invalid supported model id '{id}'.");
            }
        }
    }

    private static void ValidateUpgradePaths(UpgradePathInfo info, List<string> errors, DateOnly now)
    {
        const string where = "upgrade-paths.json";
        CheckUrl(info.OfficialToolUrl, $"{where}.official_tool_url", errors);
        CheckSourced(info.PublishedInConsultableForm, $"{where}.published_in_consultable_form", errors, now);

        if (info.PublishedInConsultableForm.Value != true && info.Paths.Count > 0)
        {
            errors.Add($"{where}: paths are listed but no official consultable source is recorded.");
        }

        foreach (var p in info.Paths)
        {
            if (p.Sources.Count == 0) errors.Add($"{where} [{p.Model} {p.From}->{p.To}]: path without source.");
            foreach (var s in p.Sources) CheckSource(s, $"{where} [{p.Model} {p.From}->{p.To}]", errors, now);
        }
    }

    private static void CheckSourced<T>(Sourced<T> s, string where, List<string> errors, DateOnly now)
    {
        switch (s.Status)
        {
            case VerificationStatus.Verified:
                if (s.Value is null) errors.Add($"{where}: verified but value is null.");
                if (s.Sources.Count == 0) errors.Add($"{where}: verified but no source.");
                break;
            case VerificationStatus.Unverified:
                if (string.IsNullOrWhiteSpace(s.Note)) errors.Add($"{where}: unverified without an explanatory note.");
                if (s.Value is not null) errors.Add($"{where}: unverified datum must not carry a value (no guessing).");
                break;
        }

        foreach (var src in s.Sources) CheckSource(src, where, errors, now);
    }

    private static void CheckSource(SourceRef src, string where, List<string> errors, DateOnly now)
    {
        if (src.EffectiveKind == SourceKind.FactoryDefaultBackup)
        {
            if (src.Url is not null) errors.Add($"{where}: a factory-default backup source must not have a URL (backups are never published).");
        }
        else if (src.Url is null)
        {
            errors.Add($"{where}: document source without URL.");
        }
        else
        {
            CheckUrl(src.Url, where, errors);
        }

        if (string.IsNullOrWhiteSpace(src.Title)) errors.Add($"{where}: source without title ({src.Url}).");
        if (src.VerifiedOn < MinVerifiedOn || src.VerifiedOn > now) errors.Add($"{where}: verified_on {src.VerifiedOn:yyyy-MM-dd} out of range.");
    }

    private static void CheckUrl(string url, string where, List<string> errors)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{where}: source URL must be absolute https ({url}).");
            return;
        }

        if (!OfficialHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{where}: '{uri.Host}' is not an official Fortinet source host.");
        }
        else if (uri.Host.Equals("fortinetweb.s3.amazonaws.com", StringComparison.OrdinalIgnoreCase)
                 && !uri.AbsolutePath.StartsWith("/docs.fortinet.com/", StringComparison.Ordinal))
        {
            errors.Add($"{where}: S3 URLs are accepted only for docs.fortinet.com attachments ({url}).");
        }
    }
}
