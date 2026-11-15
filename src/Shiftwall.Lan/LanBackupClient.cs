using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shiftwall.Features.Toolbox;

namespace Shiftwall.Lan;

/// <summary>How the FortiGate protects secrets in the backup it returns.</summary>
public enum BackupProtection
{
    /// <summary>FortiOS up to 8.0.0: no extra parameter (secrets as ENC values).</summary>
    Standard,

    /// <summary>
    /// FortiOS 8.0.1 and later: secrets encrypted with a password chosen by the user, rest in plain text
    /// (<c>encrypt_mode=sensitive</c>). The password is needed again to restore the file.
    /// </summary>
    SecretsWithPassword,
}

/// <summary>A backup request. The token and password live only in memory for the duration of the request.</summary>
public sealed record LanBackupRequest(IPAddress Address, int Port, string Token, string? Vdom, BackupProtection Protection, string? BackupPassword);

public enum LanOutcome
{
    Success,

    /// <summary>The certificate is not trusted by Windows and its fingerprint was not confirmed by the user.</summary>
    CertificateNotConfirmed,
    Unauthorized,
    Forbidden,
    NotFound,
    HttpError,
    NotAConfiguration,
    Timeout,
    NetworkError,
}

public sealed record LanBackupResult(LanOutcome Outcome, byte[]? Content = null, string? CertificateSha256 = null, string? CertificateSubject = null, int? HttpStatus = null, string? Method = null);

/// <summary>
/// Downloads the configuration backup of a FortiGate on the local network through its REST API.
/// </summary>
/// <remarks>
/// <para>Endpoint: <c>/api/v2/monitor/system/config/backup?scope=global</c> (or <c>scope=vdom&amp;vdom=…</c>) with the API
/// token in the <c>Authorization: Bearer</c> header. Sources (checked 2026-11-15): FortiOS 7.6.5 Administration Guide,
/// "Using APIs" (token in request header) and "REST API administrator"; Fortinet Community "Technical Tip: Get backup
/// config file on FortiGate using RestAPI via Python script" (endpoint, GET up to 7.6, 8.0.1 parameters
/// <c>password</c>/<c>encrypt_mode=sensitive</c>), "Technical Tip: How to get a backup config file on FortiGate by using a
/// Python script from a non-management VDOM" (<c>scope=vdom&amp;vdom=</c>) and "Troubleshooting Tip: Resolving '405 Method Not Allowed' …
/// FortiGate 7.6.0+" (POST from 7.6.0). The two Community articles disagree on the method for 7.6, so POST is tried
/// first and GET is used only when the device answers 405. The full API reference (FNDN) requires a Fortinet account.</para>
/// <para>Safety: only literal private addresses (RFC 1918, RFC 4193) are accepted, so no DNS lookup takes place; no proxy
/// is used; redirects are not followed; a certificate not trusted by Windows is accepted only if its SHA-256 fingerprint
/// was confirmed by the user. Nothing is logged.</para>
/// </remarks>
public sealed class LanBackupClient
{
    public const string Path = "/api/v2/monitor/system/config/backup";
    public const long MaxBackupBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Tests only: lets the client reach a local test server on 127.0.0.1.</summary>
    internal bool AllowLoopbackForTests { get; init; }

    /// <summary>
    /// Parses a host typed by the user. Only an IP address literal in a private range is accepted (no host names: a
    /// name would need a DNS query and could resolve to a public address).
    /// </summary>
    public static bool TryParseTarget(string text, out IPAddress address)
    {
        address = IPAddress.None;
        text = text.Trim().Trim('[', ']');
        if (!IPAddress.TryParse(text, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IpAddressRules.IsPrivate(ip)) return false;
        address = ip;
        return true;
    }

    /// <summary>Builds the request URL. The backup password (8.0.1+ mode) is a query parameter, as in Fortinet's example.</summary>
    public static Uri BuildUri(LanBackupRequest r)
    {
        var host = IpAddressRules.IsV6(r.Address) ? $"[{r.Address}]" : r.Address.ToString();
        var query = r.Vdom is { Length: > 0 } vdom ? $"scope=vdom&vdom={Uri.EscapeDataString(vdom)}" : "scope=global";
        if (r.Protection == BackupProtection.SecretsWithPassword)
        {
            query += $"&password={Uri.EscapeDataString(r.BackupPassword ?? "")}&encrypt_mode=sensitive";
        }

        return new Uri(string.Create(CultureInfo.InvariantCulture, $"https://{host}:{r.Port}{Path}?{query}"));
    }

    /// <param name="confirmedSha256">Fingerprint the user confirmed for this device, or null.</param>
    public async Task<LanBackupResult> DownloadAsync(LanBackupRequest request, string? confirmedSha256, CancellationToken ct = default)
    {
        if (!IpAddressRules.IsPrivate(request.Address) && !(AllowLoopbackForTests && IPAddress.IsLoopback(request.Address))) throw new ArgumentException("Only private addresses are allowed.", nameof(request));
        if (request.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Protection == BackupProtection.SecretsWithPassword && string.IsNullOrEmpty(request.BackupPassword))
        {
            throw new ArgumentException("A backup password is required.", nameof(request));
        }

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
                    seenFingerprint = Fingerprint(cert);
                    seenSubject = cert.Subject;
                    if (errors == SslPolicyErrors.None) return true;
                    return confirmedSha256 is not null && string.Equals(seenFingerprint, confirmedSha256, StringComparison.OrdinalIgnoreCase);
                },
            },
        };
        using var client = new HttpClient(handler) { Timeout = Timeout, MaxResponseContentBufferSize = MaxBackupBytes };

        try
        {
            var (response, method) = await SendAsync(client, request, HttpMethod.Post, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                response.Dispose();
                (response, method) = await SendAsync(client, request, HttpMethod.Get, ct).ConfigureAwait(false);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized: return new LanBackupResult(LanOutcome.Unauthorized, HttpStatus: status, Method: method);
                    case HttpStatusCode.Forbidden: return new LanBackupResult(LanOutcome.Forbidden, HttpStatus: status, Method: method);
                    case HttpStatusCode.NotFound: return new LanBackupResult(LanOutcome.NotFound, HttpStatus: status, Method: method);
                }

                if (!response.IsSuccessStatusCode) return new LanBackupResult(LanOutcome.HttpError, HttpStatus: status, Method: method);
                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                return LooksLikeConfiguration(bytes)
                    ? new LanBackupResult(LanOutcome.Success, bytes, seenFingerprint, seenSubject, status, method)
                    : new LanBackupResult(LanOutcome.NotAConfiguration, HttpStatus: status, Method: method);
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException && seenFingerprint is not null)
        {
            return new LanBackupResult(LanOutcome.CertificateNotConfirmed, CertificateSha256: seenFingerprint, CertificateSubject: seenSubject);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LanBackupResult(LanOutcome.Timeout);
        }
        catch (HttpRequestException)
        {
            return new LanBackupResult(LanOutcome.NetworkError);
        }
    }

    private static async Task<(HttpResponseMessage Response, string Method)> SendAsync(HttpClient client, LanBackupRequest request, HttpMethod method, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, BuildUri(request));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        return (response, method.Method);
    }

    /// <summary>SHA-256 of the DER certificate, upper-case hex with colons (as shown by Windows and browsers).</summary>
    public static string Fingerprint(X509Certificate cert)
    {
        var hash = SHA256.HashData(cert.GetRawCertData());
        return string.Join(':', hash.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    /// <summary>A FortiOS text backup starts with the "#config-version=" header; an API error is a JSON object.</summary>
    internal static bool LooksLikeConfiguration(byte[] bytes)
    {
        var start = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
        var head = System.Text.Encoding.ASCII.GetString(bytes, start, Math.Min(64, bytes.Length - start)).TrimStart();
        return head.StartsWith("#config-version=", StringComparison.Ordinal) || head.StartsWith("config ", StringComparison.Ordinal);
    }
}
