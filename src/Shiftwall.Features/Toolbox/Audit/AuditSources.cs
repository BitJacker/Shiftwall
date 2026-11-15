namespace Shiftwall.Features.Toolbox.Audit;

/// <summary>
/// Documents the audit rules cite. Fortinet documents were read on 2026-11-15; RFCs are the IETF documents of record.
/// </summary>
public static class AuditSources
{
    public const string VerifiedOn = "2026-11-15";

    private const string BestPracticesPdf = "https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/32eccc66-2e05-11f1-b31d-02356ffb40d9/FortiOS-8.0.0-Best_Practices.pdf";
    private const string CliReferencePdf = "https://fortinetweb.s3.amazonaws.com/docs.fortinet.com/v2/attachments/1c34e924-cbe3-11f1-8c5a-8e4c95ff11ac/FortiOS-7.6.8-CLI_Reference.pdf";

    public static readonly AuditSource BpAdministrativeSettings = new("FortiOS 8.0.0 Best Practices — Administrative settings", BestPracticesPdf);
    public static readonly AuditSource BpUserAccess = new("FortiOS 8.0.0 Best Practices — How can users access the FortiGate", BestPracticesPdf);
    public static readonly AuditSource BpExternalAdminPorts = new("FortiOS 8.0.0 Best Practices — Blocking external access to administrative ports", BestPracticesPdf);
    public static readonly AuditSource BpAdminPortsAndNames = new("FortiOS 8.0.0 Best Practices — Non-standard admin ports and administrator usernames", BestPracticesPdf);
    public static readonly AuditSource BpSecurityPolicies = new("FortiOS 8.0.0 Best Practices — Policies › Security policies", BestPracticesPdf);
    public static readonly AuditSource BpSecurityProfiles = new("FortiOS 8.0.0 Best Practices — Security profiles", BestPracticesPdf);
    public static readonly AuditSource BpLogging = new("FortiOS 8.0.0 Best Practices — Logging and reporting", BestPracticesPdf);
    public static readonly AuditSource BpStrongCiphers = new("FortiOS 8.0.0 Best Practices — Strong ciphers", BestPracticesPdf);
    public static readonly AuditSource BpEncryptedProtocols = new("FortiOS 8.0.0 Best Practices — Encrypted protocols", BestPracticesPdf);
    public static readonly AuditSource BpSystemTime = new("FortiOS 8.0.0 Best Practices — Getting started › System time", BestPracticesPdf);

    public static readonly AuditSource CliPolicy = new("FortiOS 7.6.8 CLI Reference — config firewall policy (logtraffic default: utm)", CliReferencePdf);
    public static readonly AuditSource CliGlobal = new("FortiOS 7.6.8 CLI Reference — config system global (strong-crypto, admin-https-ssl-versions, admintimeout)", CliReferencePdf);
    public static readonly AuditSource CliNtp = new("FortiOS 7.6.8 CLI Reference — config system ntp (ntpsync default: disable)", CliReferencePdf);
    public static readonly AuditSource CliDns = new("FortiOS 7.6.8 CLI Reference — config system dns (primary default: 0.0.0.0)", CliReferencePdf);
    public static readonly AuditSource CliSslVpn = new("FortiOS 7.4.10 CLI Reference — config vpn ssl settings (ssl-min-proto-ver)", "https://docs.fortinet.com/document/fortigate/7.4.10/cli-reference/114404382");

    public static readonly AuditSource Rfc8221 = new("RFC 8221 — Cryptographic Algorithm Implementation Requirements for ESP and AH", "https://www.rfc-editor.org/rfc/rfc8221");
    public static readonly AuditSource Rfc8247 = new("RFC 8247 — Algorithm Implementation Requirements for IKEv2", "https://www.rfc-editor.org/rfc/rfc8247");
    public static readonly AuditSource Rfc8996 = new("RFC 8996 — Deprecating TLS 1.0 and TLS 1.1", "https://www.rfc-editor.org/rfc/rfc8996");
}
