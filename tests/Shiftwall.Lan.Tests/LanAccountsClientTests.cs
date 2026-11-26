using System.Net;
using Shiftwall.Features.Toolbox.TwoFactor;
using static Shiftwall.Lan.Tests.LanBackupClientTests;

namespace Shiftwall.Lan.Tests;

public sealed class LanAccountsClientTests
{
    private const string Users = """{"http_method":"GET","results":[{"name":"mario.rossi","type":"password","two-factor":"disable","email-to":"","fortitoken":"","passwd":"ENC xyz"},{"name":"anna","type":"ldap","ldap-server":"AD-LAB","two-factor":"email","email-to":"anna@example.com"}],"vdom":"root","status":"success","http_status":200}""";
    private const string Admins = """{"results":[{"name":"admin","accprofile":"super_admin","two-factor":"fortitoken","fortitoken":"FTKMOB0000000001","email-to":"it@example.com","remote-auth":"disable"}],"status":"success","http_status":200}""";

    [Fact]
    public void Uris_FollowTheCmdbPaths()
    {
        var ip = IPAddress.Parse("192.168.1.99");
        Assert.Equal("https://192.168.1.99/api/v2/cmdb/user/local?vdom=Branch%201", LanAccountsClient.BuildUri(ip, 443, LanAccountsClient.UsersPath, "Branch 1").AbsoluteUri);
        Assert.Equal("https://192.168.1.99:8443/api/v2/cmdb/system/admin", LanAccountsClient.BuildUri(ip, 8443, LanAccountsClient.AdminsPath, null).AbsoluteUri);
    }

    [Fact]
    public void Json_IsMappedWithoutPasswords()
    {
        var users = LanAccountsClient.Parse(Users, AccountKind.LocalUser, "root");
        Assert.Equal(["mario.rossi", "anna"], users.Select(u => u.Name));
        Assert.Null(users[0].EmailTo);
        Assert.Equal("AD-LAB", users[1].RemoteServer);
        Assert.Equal("email", users[1].TwoFactor);
        Assert.DoesNotContain(users, u => u.ToString().Contains("xyz", StringComparison.Ordinal));
        var admin = Assert.Single(LanAccountsClient.Parse(Admins, AccountKind.Admin, null));
        Assert.True(admin.IsSuperAdmin);
        Assert.True(admin.HasFortiToken);
    }

    [Fact]
    public async Task OnlyGetRequests_AndEmptyAdminListIsFlagged()
    {
        var methods = new List<string>();
        using var server = new TestServer((method, request) =>
        {
            lock (methods) methods.Add(method);
            return request.Contains("/cmdb/system/admin", StringComparison.Ordinal) ? (200, """{"results":[]}""") : (200, Users);
        });
        var client = new LanAccountsClient { AllowLoopbackForTests = true };
        var result = await client.ReadAsync(new LanAccountsRequest(IPAddress.Loopback, server.Port, "secret-token", ["root"]), server.Fingerprint, TestContext.Current.CancellationToken);

        Assert.Equal(LanOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal("root", result.Accounts[0].Vdom);
        Assert.True(result.AdminsEmpty);
        Assert.All(methods, m => Assert.Equal("GET", m));
        Assert.Contains("Authorization: Bearer secret-token", server.LastRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntrustedCertificate_AndPublicAddress_AreRefused()
    {
        using var server = new TestServer((_, _) => (200, Users));
        var client = new LanAccountsClient { AllowLoopbackForTests = true };
        var first = await client.ReadAsync(new LanAccountsRequest(IPAddress.Loopback, server.Port, "t", null), null, TestContext.Current.CancellationToken);
        Assert.Equal(LanOutcome.CertificateNotConfirmed, first.Outcome);
        Assert.Equal(server.Fingerprint, first.CertificateSha256);

        await Assert.ThrowsAsync<ArgumentException>(() => new LanAccountsClient().ReadAsync(new LanAccountsRequest(IPAddress.Parse("8.8.8.8"), 443, "t", null), null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(401, LanOutcome.Unauthorized)]
    [InlineData(403, LanOutcome.Forbidden)]
    public async Task HttpErrors_AreReported(int status, LanOutcome expected)
    {
        using var server = new TestServer((_, _) => (status, "{}"));
        var result = await new LanAccountsClient { AllowLoopbackForTests = true }.ReadAsync(new LanAccountsRequest(IPAddress.Loopback, server.Port, "t", null), server.Fingerprint, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(LanAccountsClient.UsersPath, result.FailedPath);
    }
}
