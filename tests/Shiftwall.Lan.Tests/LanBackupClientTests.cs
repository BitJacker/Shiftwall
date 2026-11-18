using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Shiftwall.Lan.Tests;

/// <summary>Exercises the client against a minimal local HTTPS server with a self-signed certificate.</summary>
public sealed class LanBackupClientTests
{
    private const string Config = "#config-version=FGTSYN-7.6.8-FW-build9999-261101:opmode=0:vdom=0:user=admin\n#buildno=9999\nconfig system global\n    set hostname \"lab\"\nend\n";

    [Theory]
    [InlineData("192.168.1.99", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.5.4", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("203.0.113.5", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:db8::1", false)]
    [InlineData("fortigate.local", false)]
    [InlineData("", false)]
    public void OnlyPrivateAddressLiteralsAreAccepted(string host, bool ok) =>
        Assert.Equal(ok, LanBackupClient.TryParseTarget(host, out _));

    [Fact]
    public void Uri_GlobalVdomAndProtectedModes()
    {
        var ip = IPAddress.Parse("192.168.1.99");
        Assert.Equal("https://192.168.1.99/api/v2/monitor/system/config/backup?scope=global",
            LanBackupClient.BuildUri(new LanBackupRequest(ip, 443, "t", null, BackupProtection.Standard, null)).AbsoluteUri);
        Assert.Equal("https://192.168.1.99:8443/api/v2/monitor/system/config/backup?scope=vdom&vdom=Branch%201",
            LanBackupClient.BuildUri(new LanBackupRequest(ip, 8443, "t", "Branch 1", BackupProtection.Standard, null)).AbsoluteUri);
        Assert.Equal("https://192.168.1.99/api/v2/monitor/system/config/backup?scope=global&password=a%26b&encrypt_mode=sensitive",
            LanBackupClient.BuildUri(new LanBackupRequest(ip, 443, "t", null, BackupProtection.SecretsWithPassword, "a&b")).AbsoluteUri);
    }

    [Fact]
    public async Task PublicAddress_IsRejectedBeforeAnyConnection()
    {
        var client = new LanBackupClient();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.DownloadAsync(new LanBackupRequest(IPAddress.Parse("8.8.8.8"), 443, "t", null, BackupProtection.Standard, null), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UntrustedCertificate_IsRefusedUntilFingerprintConfirmed()
    {
        using var server = new TestServer((_, _) => (200, Config));
        var client = new LanBackupClient { AllowLoopbackForTests = true };
        var request = new LanBackupRequest(IPAddress.Loopback, server.Port, "secret-token", null, BackupProtection.Standard, null);

        var first = await client.DownloadAsync(request, null, TestContext.Current.CancellationToken);
        Assert.Equal(LanOutcome.CertificateNotConfirmed, first.Outcome);
        Assert.Equal(server.Fingerprint, first.CertificateSha256);
        Assert.Null(first.Content);

        var second = await client.DownloadAsync(request, first.CertificateSha256, TestContext.Current.CancellationToken);
        Assert.Equal(LanOutcome.Success, second.Outcome);
        Assert.Equal(Config, Encoding.UTF8.GetString(second.Content!));
        Assert.Equal("POST", second.Method);
        Assert.Contains("Authorization: Bearer secret-token", server.LastRequest);
        Assert.StartsWith("POST /api/v2/monitor/system/config/backup?scope=global ", server.LastRequest);
    }

    [Fact]
    public async Task MethodNotAllowed_FallsBackToGet()
    {
        using var server = new TestServer((method, _) => method == "POST" ? (405, "{\"http_status\":405}") : (200, Config));
        var client = new LanBackupClient { AllowLoopbackForTests = true };
        var request = new LanBackupRequest(IPAddress.Loopback, server.Port, "t", null, BackupProtection.Standard, null);
        var result = await client.DownloadAsync(request, server.Fingerprint, TestContext.Current.CancellationToken);
        Assert.Equal(LanOutcome.Success, result.Outcome);
        Assert.Equal("GET", result.Method);
    }

    [Theory]
    [InlineData(401, LanOutcome.Unauthorized)]
    [InlineData(403, LanOutcome.Forbidden)]
    [InlineData(404, LanOutcome.NotFound)]
    [InlineData(500, LanOutcome.HttpError)]
    public async Task HttpErrors_AreReported(int status, LanOutcome expected)
    {
        using var server = new TestServer((_, _) => (status, "{}"));
        var client = new LanBackupClient { AllowLoopbackForTests = true };
        var result = await client.DownloadAsync(new LanBackupRequest(IPAddress.Loopback, server.Port, "t", null, BackupProtection.Standard, null), server.Fingerprint, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task JsonInsteadOfBackup_IsNotAConfiguration()
    {
        using var server = new TestServer((_, _) => (200, "{\"status\":\"error\"}"));
        var client = new LanBackupClient { AllowLoopbackForTests = true };
        var result = await client.DownloadAsync(new LanBackupRequest(IPAddress.Loopback, server.Port, "t", null, BackupProtection.Standard, null), server.Fingerprint, TestContext.Current.CancellationToken);
        Assert.Equal(LanOutcome.NotAConfiguration, result.Outcome);
    }

    /// <summary>One-request-per-connection HTTPS server for tests (TLS with a fresh self-signed certificate).</summary>
    private sealed class TestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _cert;
        private readonly Func<string, string, (int Status, string Body)> _respond;
        private readonly CancellationTokenSource _cts = new();

        public TestServer(Func<string, string, (int, string)> respond)
        {
            _respond = respond;
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var req = new CertificateRequest("CN=FGTSYN-test", key, HashAlgorithmName.SHA256);
            using var temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _cert = X509CertificateLoader.LoadPkcs12(temp.Export(X509ContentType.Pkcs12), null);
            Fingerprint = LanBackupClient.Fingerprint(_cert);
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string Fingerprint { get; }

        public string LastRequest { get; private set; } = "";

        private async Task Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (tcp)
                    {
                        try
                        {
                            using var ssl = new SslStream(tcp.GetStream());
                            await ssl.AuthenticateAsServerAsync(_cert);
                            var buffer = new byte[8192];
                            var sb = new StringBuilder();
                            while (!sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                            {
                                var n = await ssl.ReadAsync(buffer);
                                if (n == 0) return;
                                sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                            }

                            LastRequest = sb.ToString();
                            var method = LastRequest.Split(' ')[0];
                            var (status, body) = _respond(method, LastRequest);
                            var bytes = Encoding.UTF8.GetBytes(body);
                            var head = $"HTTP/1.1 {status} X\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                            await ssl.WriteAsync(Encoding.ASCII.GetBytes(head));
                            await ssl.WriteAsync(bytes);
                            await ssl.FlushAsync();
                        }
                        catch (Exception)
                        {
                            // Client aborted the handshake (untrusted certificate): expected in one test.
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cert.Dispose();
            _cts.Dispose();
        }
    }
}
