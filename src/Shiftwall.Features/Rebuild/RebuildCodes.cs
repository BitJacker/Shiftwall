namespace Shiftwall.Features.Rebuild;

/// <summary>Stable codes of the rebuild migration; texts in Resources/MigrationMessages(.it).resx.</summary>
public static class RebuildCodes
{
    public const string MultiVdomNotSupported = "RB-001";
    public const string RemovedNoEquivalent = "RB-002";
    public const string RemovedWithEquivalent = "RB-003";
    public const string SecretNotMigrated = "RB-004";
    public const string UnresolvedReference = "RB-005";
    public const string ParamNotInTarget = "RB-006";
    public const string InvalidValue = "RB-007";
    public const string UnpastableCharacter = "RB-008";
    public const string PathNotInTarget = "RB-009";
    public const string CircularGroup = "RB-010";
    public const string SecretRequired = "RB-011";
    public const string DuplicateTarget = "RB-012";
    public const string SwitchWouldBeEmpty = "RB-013";
    public const string PortsRemovedFromSwitch = "RB-014";
    public const string UnsupportedInterfaceType = "RB-015";
    public const string InterfaceNotMapped = "RB-016";
    public const string InterfaceNotMappedUnused = "RB-017";
    public const string NameTakenOnTarget = "RB-018";
    public const string InvalidPlanAddress = "RB-019";
    public const string ManagementLockout = "RB-020";
    public const string ManagementLast = "RB-021";
    public const string PolicyBasedVpn = "RB-022";
    public const string IpsecPeerCheck = "RB-023";
    public const string GatewayOutsideSubnet = "RB-024";
    public const string ProfilesNotMigrated = "RB-025";
    public const string CurrentAdmin = "RB-026";
    public const string FactoryDhcpUnknown = "RB-027";
    public const string SectionNotRebuilt = "RB-028";
    public const string FactoryDefaultTarget = "RB-029";
    public const string ManagementUnknown = "RB-030";
    public const string LowMemoryModel = "RB-031";
    public const string SecretsReset = "RB-032";
    public const string NoDefaultBackup = "RB-033";
    public const string SslVpnNotConverted = "RB-034";
    public const string SslVpnPolicyNotConverted = "RB-035";
    public const string SslVpnNoListenInterface = "RB-036";
    public const string SslVpnGroups = "RB-037";
    public const string SslVpnNoPool = "RB-038";
    public const string SslVpnConverted = "RB-039";
    public const string ForticlientChecklist = "RB-040";
    public const string ModeRebuild = "RB-041";
    public const string ModeRestore = "RB-042";
    public const string ModeForced = "RB-043";
    public const string GlobalPartial = "RB-044";
    public const string BrokenReference = "RB-045";
    public const string RadiusMessageAuthenticator = "RB-046";
}
