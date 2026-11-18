namespace Shiftwall.Core.Config;

/// <summary>An interface declared in <c>config system interface</c>.</summary>
/// <param name="Type">Value of <c>set type</c>, or <c>vlan</c> when absent (FortiOS default per the CLI reference).</param>
public sealed record DeclaredInterface(string Name, string Type, string? Vdom, int Line);

/// <summary>A <c>config system virtual-switch</c> entry with its member ports.</summary>
public sealed record VirtualSwitch(string Name, IReadOnlyList<string> Members, int Line);

/// <summary>
/// Reads the interface inventory of a configuration (global scope).
/// </summary>
/// <remarks>
/// In <c>config system interface</c> the default <c>type</c> is <c>vlan</c> (FortiOS CLI Reference, config system
/// interface) and backups only show non-default values (Administration Guide, "Subcommands": <c>show</c>), so physical
/// ports always carry an explicit <c>set type physical</c>.
/// </remarks>
public static class InterfaceInventory
{
    public const string DefaultType = "vlan";

    public static IReadOnlyList<DeclaredInterface> Declared(ConfigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var block = doc.GlobalScope.Block("system interface");
        if (block is null) return [];
        return
        [
            .. block.Entries.Select(e => new DeclaredInterface(
                e.Key,
                e.Field("type") is { Verb: "set" } t && t.Values.Count > 0 ? t.Values[0] : DefaultType,
                e.Field("vdom") is { Verb: "set" } v && v.Values.Count > 0 ? v.Values[0] : null,
                e.Statement.Line)),
        ];
    }

    public static IReadOnlyList<DeclaredInterface> Physical(ConfigDocument doc) =>
        [.. Declared(doc).Where(i => i.Type == "physical")];

    public static IReadOnlyList<VirtualSwitch> VirtualSwitches(ConfigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var block = doc.GlobalScope.Block("system virtual-switch");
        if (block is null) return [];
        return
        [
            .. block.Entries.Select(e => new VirtualSwitch(
                e.Key,
                [.. e.Block("port")?.Entries.Select(p => p.Key) ?? []],
                e.Statement.Line)),
        ];
    }
}
