using System.Text.RegularExpressions;
using Shiftwall.Core.Config;
using Shiftwall.Features.Migration;

namespace Shiftwall.Features.Rebuild;

/// <summary>Builds the rebuild target from what the wizard knows (factory-default backup or model database).</summary>
public static partial class RebuildTargets
{
    /// <summary>
    /// Models with 2 GB RAM, from FortiOS 8.0.2 Release Notes, "Agentless VPN (formerly SSL VPN web mode) not supported
    /// on some FortiGate series models" (40F, 50G, 60F, 61F, 70G, 90G/91G and their variants; FGR-60F 2 GB only).
    /// </summary>
    private static readonly string[] LowMemoryPrefixes = ["FG-40F", "FWF-40F", "FG-50G", "FWF-50G", "FG-51G", "FWF-51G", "FG-60F", "FWF-60F", "FG-61F", "FWF-61F", "FG-70G", "FWF-70G", "FG-71G", "FWF-71G", "FG-90G", "FG-91G"];

    public const string LowMemorySource = "https://docs.fortinet.com/document/fortigate/8.0.2/fortios-release-notes/877104/agentless-vpn-formerly-ssl-vpn-web-mode-not-supported-on-some-fortigate-series-models";

    public static bool IsLowMemory(string? modelId) =>
        modelId is not null && LowMemoryPrefixes.Any(p => modelId.Equals(p, StringComparison.OrdinalIgnoreCase) || modelId.StartsWith(p + "-", StringComparison.OrdinalIgnoreCase));

    /// <summary>"FGT40F" (backup header) → "FG-40F"; "FWF60F" → "FWF-60F".</summary>
    public static string? ModelFromPlatform(string? platform)
    {
        if (platform is null) return null;
        var m = PlatformRegex().Match(platform);
        return m.Success ? (m.Groups[1].Value == "FGT" ? "FG-" : "FWF-") + m.Groups[2].Value : null;
    }

    [GeneratedRegex("^(FGT|FWF)([0-9]+[A-Z]+[0-9A-Z]*)$")]
    private static partial Regex PlatformRegex();

    public static RebuildTarget From(TargetProfile target, ConfigDocument? defaultBackup)
    {
        ArgumentNullException.ThrowIfNull(target);
        var model = target.ModelId ?? ModelFromPlatform(target.PlatformCode);
        IReadOnlyList<string> interfaces = defaultBackup is null
            ? target.PhysicalInterfaces
            : [.. InterfaceInventory.Declared(defaultBackup).Select(i => i.Name)];
        return new RebuildTarget(model, target.Version ?? "", interfaces, target.VirtualSwitches, defaultBackup, IsLowMemory(model));
    }
}
