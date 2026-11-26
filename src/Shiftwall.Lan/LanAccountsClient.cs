using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using Shiftwall.Features.Toolbox;
using Shiftwall.Features.Toolbox.TwoFactor;

namespace Shiftwall.Lan;

/// <summary>A read-only request for the accounts of a FortiGate. The token lives only in memory.</summary>
/// <param name="Vdoms">VDOMs whose local users are read (one request each); empty or null for a single-VDOM unit.</param>
public sealed record LanAccountsRequest(IPAddress Address, int Port, string Token, IReadOnlyList<string>? Vdoms);

/// <param name="AdminsEmpty">The administrator list came back empty: per Fortinet, a REST API administrator whose profile is not super_admin gets an empty list.</param>
public sealed record LanAccountsResult(LanOutcome Outcome, IReadOnlyList<AccountEntry> Accounts, bool AdminsEmpty = false, string? CertificateSha256 = null, string? CertificateSubject = null, int? HttpStatus = null, string? FailedPath = null);

/// <summary>
/// Reads local users and administrators through the FortiOS REST API, GET only. Nothing is ever written.
/// </summary>
/// <remarks>
/// <para>Endpoints (checked 2026-11-26): <c>/api/v2/cmdb/user/local</c> (with <c>?vdom=</c>) and <c>/api/v2/cmdb/system/admin</c>.
/// The cmdb paths mirror the CLI tables (FortiOS 7.6.4 Administration Guide, "Using APIs": <c>api/v2/cmdb/firewall/address</c>,
/// token in the <c>Authorization: Bearer</c> header). Fortinet Community technical tips use both endpoints: "Technical Tip:
/// How to change local user's password using API" (cmdb/user/local, 7.4) and "Technical Tip: How to show super admin user via
/// REST API Admin" (cmdb/system/admin returns an empty result unless the API administrator's profile is super_admin). The JSON
/// keys are the CLI field names. The complete API reference (FNDN) needs a Fortinet account and was not consulted.</para>
/// <para>Writing through the API is not implemented: the write behaviour of these endpoints is documented only on FNDN, and the
/// tips report restrictions (passwords cannot be changed with PUT). Changes go through the generated CLI scripts.</para>
/// <para>Safety as in <see cref="LanBackupClient"/>: private address literals only, no proxy, no redirects, untrusted
/// certificates only after the user confirmed the SHA-256 fingerprint, nothing logged.</para>
/// </remarks>
public sealed class LanAccountsClient
{
    public const string UsersPath = "/api/v2/cmdb/user/local";
    public const string AdminsPath = "/api/v2/cmdb/system/admin";
    public const long MaxResponseBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    internal bool AllowLoopbackForTests { get; init; }

    public static Uri BuildUri(IPAddress address, int port, string path, string? vdom)
    {
        var host = IpAddressRules.IsV6(address) ? $"[{address}]" : address.ToString();
        var query = vdom is { Length: > 0 } ? "?vdom=" + Uri.EscapeDataString(vdom) : "";
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"https://{host}:{port}{path}{query}"));
    }

    public async Task<LanAccountsResult> ReadAsync(LanAccountsRequest request, string? confirmedSha256, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IpAddressRules.IsPrivate(request.Address) && !(AllowLoopbackForTests && IPAddress.IsLoopback(request.Address))) throw new ArgumentException("Only private addresses are allowed.", nameof(request));
        if (request.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(request));

        string? seenFingerprint = null;
        string? seenSubject = null;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                {
                    if (cert is null) return false;
                    seenFingerprint = LanBackupClient.Fingerprint(cert);
                    seenSubject = cert.Subject;
                    if (errors == SslPolicyErrors.None) return true;
                    return confirmedSha256 is not null && string.Equals(seenFingerprint, confirmedSha256, StringComparison.OrdinalIgnoreCase);
                },
            },
        };
        using var client = new HttpClient(handler) { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
        var accounts = new List<AccountEntry>();
        var vdoms = request.Vdoms is { Count: > 0 } v ? v.Select(x => (string?)x).ToList() : [null];
        var multi = request.Vdoms is { Count: > 0 };

        try
        {
            foreach (var vdom in vdoms)
            {
                var (status, json) = await GetAsync(client, BuildUri(request.Address, request.Port, UsersPath, vdom), request.Token, ct).ConfigureAwait(false);
                if (Fail(status) is { } outcome) return new LanAccountsResult(outcome, [], HttpStatus: status, FailedPath: UsersPath);
                accounts.AddRange(Parse(json!, AccountKind.LocalUser, multi ? vdom : null));
            }

            var (adminStatus, adminJson) = await GetAsync(client, BuildUri(request.Address, request.Port, AdminsPath, null), request.Token, ct).ConfigureAwait(false);
            if (Fail(adminStatus) is { } adminOutcome) return new LanAccountsResult(adminOutcome, [], HttpStatus: adminStatus, FailedPath: AdminsPath);
            var admins = Parse(adminJson!, AccountKind.Admin, null);
            accounts.AddRange(admins);
            return new LanAccountsResult(LanOutcome.Success, accounts, admins.Count == 0, seenFingerprint, seenSubject, 200);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException && seenFingerprint is not null)
        {
            return new LanAccountsResult(LanOutcome.CertificateNotConfirmed, [], CertificateSha256: seenFingerprint, CertificateSubject: seenSubject);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LanAccountsResult(LanOutcome.Timeout, []);
        }
        catch (HttpRequestException)
        {
            return new LanAccountsResult(LanOutcome.NetworkError, []);
        }
        catch (JsonException)
        {
            return new LanAccountsResult(LanOutcome.NotAConfiguration, []);
        }
    }

    private static LanOutcome? Fail(int status) => status switch
    {
        200 => null,
        401 => LanOutcome.Unauthorized,
        403 => LanOutcome.Forbidden,
        404 => LanOutcome.NotFound,
        _ => LanOutcome.HttpError,
    };

    private static async Task<(int Status, string? Body)> GetAsync(HttpClient client, Uri uri, string token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        return status == 200 ? (status, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : (status, null);
    }

    /// <summary>Maps the <c>results</c> array of a cmdb answer. Password fields are never read.</summary>
    internal static List<AccountEntry> Parse(string json, AccountKind kind, string? vdom)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var list = new List<AccountEntry>();
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) throw new JsonException("No results array.");
        foreach (var r in results.EnumerateArray())
        {
            string? S(string name) => r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s ? s : null;
            var name = S("name");
            if (name is null) continue;
            if (kind == AccountKind.LocalUser)
            {
                var type = S("type") ?? "password";
                var server = type switch { "ldap" => S("ldap-server"), "radius" => S("radius-server"), "tacacs+" => S("tacacs+-server"), "saml" => S("saml-server"), _ => null };
                list.Add(new AccountEntry(kind, vdom, name, type, server, S("two-factor"), S("email-to"), S("fortitoken"), null, 0));
            }
            else
            {
                var remote = S("remote-auth") == "enable";
                list.Add(new AccountEntry(kind, null, name, remote ? "remote" : "password", remote ? S("remote-group") : null, S("two-factor"), S("email-to"), S("fortitoken"), S("accprofile"), 0));
            }
        }

        return list;
    }
}
