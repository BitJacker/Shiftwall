using System.Text;
using Shiftwall.Features.Toolbox.Export;
using Shiftwall.Features.Toolbox.TwoFactor;
using static Shiftwall.Features.Tests.Toolbox.TwoFactor.TwoFactorFixtures;

namespace Shiftwall.Features.Tests.Toolbox.TwoFactor;

public sealed class TwoFactorTests
{
    private static ChangeRequest Set(string user, string? email, int row = 0) => new(row, user, email, ChangeAction.Set);

    private static AccountState State(string config) => AccountState.FromConfig(Doc(config));

    /// <summary>Applies every batch, then every rollback in reverse order.</summary>
    private static (AccountState After, AccountState Restored) ApplyAndRollBack(string config, ScriptBundle bundle)
    {
        var after = bundle.Scripts.Aggregate(State(config), (s, f) => s.Apply(f.Text));
        var restored = bundle.Rollbacks.Reverse().Aggregate(after, (s, f) => s.Apply(f.Text));
        return (after, restored);
    }

    [Fact]
    public void Inventory_ReadsUsersAdminsServersAndGroups()
    {
        var inv = Inventory(Config(50));

        Assert.Equal(50, inv.Users.Count());
        Assert.Equal(3, inv.Admins.Count());
        Assert.Equal("ldap", inv.Find(AccountKind.LocalUser, null, "user0010")!.Type);
        Assert.Equal("AD-LAB", inv.Find(AccountKind.LocalUser, null, "user0010")!.RemoteServer);
        Assert.Equal("radius", inv.Find(AccountKind.LocalUser, null, "user0015")!.Type);
        Assert.Equal("FTKMOB0000000003", inv.Find(AccountKind.LocalUser, null, "user0003")!.FortiToken);
        Assert.Equal("user0007@example.com", inv.Find(AccountKind.LocalUser, null, "user0007")!.EmailTo);
        var netops = inv.Find(AccountKind.Admin, null, "netops")!;
        Assert.True(netops.HasFortiToken);
        Assert.False(netops.IsSuperAdmin);
        Assert.True(inv.EmailServer.IsUsable);
        Assert.Contains(inv.RemoteGroups, g => g.Name == "VPN-AD");
    }

    [Fact]
    public void Golden_OneUser()
    {
        var config = Config(1);
        var inv = Inventory(config);
        var plan = TwoFactorPlanner.Plan(inv, [Set("user0001", "user0001@example.com", 2)]);

        Assert.False(plan.IsBlocked, string.Join("; ", plan.Findings.Select(f => f.Format())));
        var bundle = TwoFactorScripts.Generate(plan, inv, new ScriptOptions(SourceName: "lab-1.conf"));
        AssertGolden("one-user.txt", All(bundle));
        var (after, restored) = ApplyAndRollBack(config, bundle);
        Assert.Equal(["email"], after.Entries[("", "user local", "user0001")]["two-factor"]);
        Assert.True(restored.SameAs(State(config)));
    }

    [Fact]
    public void Golden_FiftyUsers_WithTimersAndBatchesOf20()
    {
        var config = Config(50);
        var inv = Inventory(config);
        var csv = new StringBuilder("username,email,tipo,server_remoto,azione\n");
        for (var n = 1; n <= 50; n++)
        {
            if (n == 3) continue; // FortiToken user: covered by its own test
            csv.Append(Name(n)).Append(n % 7 == 0 ? ",,,,abilita2fa\n" : $",{Name(n)},,,set\n");
        }

        var (requests, errors) = TwoFactorInput.ParseCsv(csv.ToString());
        Assert.Empty(errors);
        var plan = TwoFactorPlanner.Plan(inv, requests, new PlanOptions(new EmailRule("example.com")));
        Assert.False(plan.IsBlocked, string.Join("; ", plan.Findings.Select(f => f.Format())));
        Assert.Equal(49, plan.Items.Count);

        var bundle = TwoFactorScripts.Generate(plan, inv, new ScriptOptions(BatchSize: 20, IncludeTimers: true, SourceName: "lab-50.conf"));
        Assert.Equal(3, bundle.Scripts.Count);
        AssertGolden("fifty-users.txt", All(bundle));
        var (after, restored) = ApplyAndRollBack(config, bundle);
        Assert.Equal(["300"], after.Settings[("", "system global")]["two-factor-email-expiry"]);
        Assert.True(restored.SameAs(State(config)));
    }

    [Fact]
    public void ThousandUsers_DefaultBatchOf500_RollbackRestoresTheOriginal()
    {
        var config = Config(1000);
        var inv = Inventory(config);
        var requests = Enumerable.Range(1, 1000).Where(n => n != 3).Select(n => Set(Name(n), Name(n) + "@example.com", n + 1)).ToList();
        var plan = TwoFactorPlanner.Plan(inv, requests);

        Assert.False(plan.IsBlocked);
        var bundle = TwoFactorScripts.Generate(plan, inv);
        Assert.Equal([500, 499], bundle.Scripts.Select(s => s.Accounts));
        Assert.Equal(["2fa-users-001.txt", "2fa-users-002.txt"], bundle.Scripts.Select(s => s.FileName));
        Assert.StartsWith("# Shiftwall - email two-factor authentication - batch 1 of 2 (local users)\n", bundle.Scripts[0].Text, StringComparison.Ordinal);
        Assert.Contains("# Accounts in this file: 500\n", bundle.Scripts[0].Text, StringComparison.Ordinal);

        var (after, restored) = ApplyAndRollBack(config, bundle);
        Assert.All(after.Entries.Where(e => e.Key.Table == "user local" && e.Key.Entry != "user0003"), e => Assert.Equal(["email"], e.Value["two-factor"]));
        Assert.True(restored.SameAs(State(config)));
    }

    [Fact]
    public void MultiVdom_ScriptsAreWrapped_AndAmbiguousNamesNeedTheVdom()
    {
        var config = Config(20, multiVdom: true);
        var inv = Inventory(config);
        Assert.Equal(["root", "branch"], inv.Vdoms);
        Assert.Equal("branch", inv.Find(AccountKind.LocalUser, "branch", "user0015")!.Vdom);

        var plan = TwoFactorPlanner.Plan(inv, [Set("user0001", "user0001@example.com", 2), Set("user0015", "user0015@example.com", 3), new(4, "netops", "netops@example.com", ChangeAction.EnableTwoFactor, Kind: AccountKind.Admin)],
            new PlanOptions(ConfirmedFortiToken: new HashSet<AccountKey> { new(AccountKind.Admin, null, "netops") }, AdminFirstConfirmation: true, AdminSecondConfirmation: true));
        Assert.False(plan.IsBlocked, string.Join("; ", plan.Findings.Select(f => f.Format())));
        var bundle = TwoFactorScripts.Generate(plan, inv, new ScriptOptions(SourceName: "lab-vdom.conf"));
        AssertGolden("multi-vdom.txt", All(bundle));
        var (after, restored) = ApplyAndRollBack(config, bundle);
        Assert.Equal(["email"], after.Entries[("vdom:branch", "user local", "user0015")]["two-factor"]);
        Assert.Equal(["email"], after.Entries[("global", "system admin", "netops")]["two-factor"]);
        Assert.Equal(["FTKMOB0000000001"], after.Entries[("global", "system admin", "netops")]["fortitoken"]);
        Assert.True(restored.SameAs(State(config)));

        // The same user name in both VDOMs.
        var twin = config.Replace("edit \"user0011\"", "edit \"user0001\"", StringComparison.Ordinal);
        var ambiguous = TwoFactorPlanner.Plan(Inventory(twin), [Set("user0001", "a@example.com", 2)]);
        Assert.Contains(ambiguous.Findings, f => f.Code == "Ambiguous" && f.Severity == PlanSeverity.Error);
        var withVdom = TwoFactorPlanner.Plan(Inventory(twin), [new(2, "user0001", "a@example.com", ChangeAction.Set, Vdom: "branch")]);
        Assert.Equal("branch", Assert.Single(withVdom.Items).Key.Vdom);
    }

    [Fact]
    public void DirectoryUsers_AreCreatedAsLocalLdapUsersOnlyWhenAsked()
    {
        var inv = Inventory(Config(5));
        var request = new ChangeRequest(2, "anna.verdi", "anna.verdi@example.com", ChangeAction.Set, "ldap", "AD-LAB");

        var off = TwoFactorPlanner.Plan(inv, [request]);
        Assert.Contains(off.Findings, f => f.Code == "DirectoryOnly" && f.Severity == PlanSeverity.Error);
        Assert.Empty(off.Items);

        var on = TwoFactorPlanner.Plan(inv, [request], new PlanOptions(CreateMissingUsers: true));
        Assert.False(on.IsBlocked);
        var script = TwoFactorScripts.Generate(on, inv);
        Assert.Contains("        set type ldap\n        set ldap-server \"AD-LAB\"\n        set email-to \"anna.verdi@example.com\"\n        set two-factor email\n", script.Scripts[0].Text, StringComparison.Ordinal);
        Assert.Contains("    delete \"anna.verdi\"\n", script.Rollbacks[0].Text, StringComparison.Ordinal);

        var badServer = TwoFactorPlanner.Plan(inv, [request with { RemoteServer = "NOPE" }], new PlanOptions(CreateMissingUsers: true));
        Assert.Contains(badServer.Findings, f => f.Code == "UnknownRemoteServer");
        Assert.Contains(off.Findings, f => f.Code == "RemoteGroup");
    }

    [Fact]
    public void FortiTokenHolders_NeedARowConfirmation_AndKeepTheirSerial()
    {
        var config = Config(5);
        var inv = Inventory(config);
        var key = new AccountKey(AccountKind.LocalUser, null, "user0003");

        var unconfirmed = TwoFactorPlanner.Plan(inv, [Set("user0003", "user0003@example.com", 2)]);
        Assert.True(unconfirmed.IsBlocked);
        Assert.Contains(unconfirmed.Findings, f => f.Code == "TokenConfirm" && f.Severity == PlanSeverity.NeedsConfirmation && f.Row == 2);
        Assert.Throws<InvalidOperationException>(() => TwoFactorScripts.Generate(unconfirmed, inv));

        var confirmed = TwoFactorPlanner.Plan(inv, [Set("user0003", "user0003@example.com", 2)], new PlanOptions(ConfirmedFortiToken: new HashSet<AccountKey> { key }));
        Assert.False(confirmed.IsBlocked);
        var bundle = TwoFactorScripts.Generate(confirmed, inv);
        Assert.DoesNotContain("fortitoken", bundle.Scripts[0].Text.Replace("# ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("set two-factor fortitoken", bundle.Rollbacks[0].Text, StringComparison.Ordinal);
        var (after, restored) = ApplyAndRollBack(config, bundle);
        Assert.Equal(["FTKMOB0000000003"], after.Entries[("", "user local", "user0003")]["fortitoken"]);
        Assert.True(restored.SameAs(State(config)));
    }

    [Fact]
    public void Admins_AreProtectedAgainstLockOut()
    {
        var inv = Inventory(Config(2));
        AccountKey Admin(string n) => new(AccountKind.Admin, null, n);
        var both = new List<ChangeRequest>
        {
            new(2, "admin", "it-admin@example.com", ChangeAction.Set, Kind: AccountKind.Admin),
            new(3, "breakglass", "someone@gmail.com", ChangeAction.Set, Kind: AccountKind.Admin),
        };

        var plan = TwoFactorPlanner.Plan(inv, both, new PlanOptions(AdminFirstConfirmation: true, AdminSecondConfirmation: true));
        Assert.Contains(plan.Findings, f => f.Code == "AllSuperAdmins" && f.Severity == PlanSeverity.Error);
        Assert.Contains(plan.Findings, f => f.Code == "AdminExternalEmail");
        Assert.Contains(plan.Findings, f => f.Code == "BreakGlass");
        Assert.Contains(plan.Findings, f => f.Code == "TestOneAdmin");
        Assert.True(plan.IsBlocked);

        var one = TwoFactorPlanner.Plan(inv, [both[0]], new PlanOptions(AdminFirstConfirmation: true));
        Assert.Contains(one.Findings, f => f.Code == "AdminConfirm" && f.Severity == PlanSeverity.NeedsConfirmation);
        Assert.DoesNotContain(one.Findings, f => f.Code == "AllSuperAdmins");

        var ok = TwoFactorPlanner.Plan(inv, [both[0]], new PlanOptions(AdminFirstConfirmation: true, AdminSecondConfirmation: true));
        Assert.False(ok.IsBlocked);
        var bundle = TwoFactorScripts.Generate(ok, inv);
        Assert.Equal("2fa-admins-001.txt", Assert.Single(bundle.Scripts).FileName);
        Assert.StartsWith("config system admin\n    edit \"admin\"\n", bundle.Scripts[0].Text.Split("\n").SkipWhile(l => l.StartsWith('#')).Aggregate("", (a, l) => a + l + "\n"), StringComparison.Ordinal);
        Assert.Equal(Admin("admin"), ok.Items[0].Key);
    }

    [Fact]
    public void MissingEmailServer_BlocksGeneration()
    {
        var inv = Inventory(Config(3, emailServer: false));
        var plan = TwoFactorPlanner.Plan(inv, [Set("user0001", "user0001@example.com", 2)]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Findings, f => f.Code == "EmailServerMissing" && f.Severity == PlanSeverity.Error);
        Assert.Throws<InvalidOperationException>(() => TwoFactorScripts.Generate(plan, inv));
    }

    [Fact]
    public void CsvWithErrors_ReportsEveryProblemByRow()
    {
        var inv = Inventory(Config(10));
        const string csv = """
            username;email;tipo;server_remoto;azione
            user0001;not-an-email;;;set
            ghost;ghost@example.com;;;set
            user0002;user0002@example.com;;;set
            user0002;user0002@example.com;;;set
            user0004;a@example.com;;;set
            user0004;b@example.com;;;set
            user0005;user0005@example.com;;;boh
            ;x@example.com;;;set
            user0006;aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@example.com;;;set
            user0008;shared@example.com;;;set
            user0009;shared@example.com;;;set
            user0010;;;;modifica
            """;
        var (requests, errors) = TwoFactorInput.ParseCsv(csv);
        Assert.Contains(errors, e => e.Code == "InvalidAction" && e.Row == 8);
        var plan = TwoFactorPlanner.Plan(inv, requests);
        var codes = plan.Findings.Where(f => f.Row is not null).Select(f => (f.Row, f.Code)).ToList();

        Assert.Contains((2, "InvalidEmail"), codes);
        Assert.Contains((3, "UnknownAccount"), codes);
        Assert.Contains((5, "Duplicate"), codes);
        Assert.Contains((6, "Conflict"), codes);
        Assert.Contains((7, "Conflict"), codes);
        Assert.Contains((9, "EmptyUsername"), codes);
        Assert.Contains((10, "EmailTooLong"), codes);
        Assert.Contains((13, "EmailRequired"), codes);
        Assert.Contains(plan.Findings, f => f.Code == "SharedEmail");
        Assert.Equal(["user0002", "user0008", "user0009"], plan.Items.Select(i => i.Key.Name).Order());
        Assert.All(plan.Findings.Concat(errors), f => Assert.DoesNotContain("TwoFactor.", f.Format(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("mario.rossi", null, "azienda.it", null, "mario.rossi@azienda.it")]
    [InlineData("mario.rossi", "m.rossi", "azienda.it", null, "m.rossi@azienda.it")]
    [InlineData("mario.rossi", "mario@altro.it", "azienda.it", null, "mario@altro.it")]
    [InlineData("Mario_Rossi", null, "azienda.it", "{name}.{surname}", "mario.rossi@azienda.it")]
    [InlineData("mrossi", null, null, "{username}@azienda.it", "mrossi@azienda.it")]
    [InlineData("mrossi", null, "azienda.it", "{name}.{surname}", null)]
    public void EmailRule_CompletesDomainAndTemplate(string user, string? email, string? domain, string? template, string? expected)
    {
        var rule = new EmailRule(domain, template);
        var input = email ?? (template is null ? user : null);
        Assert.Equal(expected, rule.Apply(user, input, out _));
    }

    [Fact]
    public void NewPasswordUsers_PasswordsNeverReachScriptsReportsOrFindings()
    {
        var inv = Inventory(Config(5));
        var requests = Enumerable.Range(1, 30).Select(n => new ChangeRequest(n + 1, $"new{n:00}", $"new{n:00}@example.com", ChangeAction.Set, "password")).ToList();
        var plan = TwoFactorPlanner.Plan(inv, requests, new PlanOptions(CreateMissingUsers: true));
        Assert.False(plan.IsBlocked);
        var bundle = TwoFactorScripts.Generate(plan, inv, new ScriptOptions(BatchSize: 10));
        Assert.Equal(30, bundle.Passwords.Count);
        Assert.Equal(30, bundle.Passwords.Select(p => p.Password).Distinct().Count());

        var texts = new List<string>
        {
            TwoFactorReport.Html(plan, [], inv, "lab.conf", bundle.Passwords.Count),
            TwoFactorReport.Csv(plan, []),
            CsvWriter.Write(TwoFactorReport.Inventory(inv)),
        };
        texts.AddRange(bundle.Scripts.Select(s => s.Text));
        texts.AddRange(bundle.Rollbacks.Select(s => s.Text));
        texts.AddRange(plan.Findings.Select(f => f.Format()));
        foreach (var p in bundle.Passwords)
        {
            Assert.All(texts, t => Assert.DoesNotContain(p.Password, t, StringComparison.Ordinal));
        }

        Assert.All(bundle.Scripts.Concat(bundle.Rollbacks), s => Assert.DoesNotContain("passwd", s.Text, StringComparison.Ordinal));
        Assert.Contains("        set status disable\n", bundle.Scripts[0].Text, StringComparison.Ordinal);

        const string vaultPassword = "correct horse battery";
        var vault = PasswordVault.Encrypt(bundle.Passwords, inv, vaultPassword);
        var raw = Encoding.Latin1.GetString(vault);
        Assert.All(bundle.Passwords, p => Assert.DoesNotContain(p.Password, raw, StringComparison.Ordinal));
        var entries = PasswordVault.Decrypt(vault, vaultPassword);
        Assert.Equal(bundle.Passwords.Select(p => p.Password), entries.Select(e => e.Password));
        Assert.Contains("set passwd", entries[0].Cli, StringComparison.Ordinal);
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(() => PasswordVault.Decrypt(vault, "wrong password!!"));
    }

    [Fact]
    public void GeneratedPasswords_AreStrong()
    {
        for (var i = 0; i < 50; i++)
        {
            var p = TwoFactorScripts.NewPassword();
            Assert.Equal(20, p.Length);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
            Assert.DoesNotContain(p, c => c is '"' or '\'' or '\\' or ' ');
        }
    }

    [Fact]
    public void Excel_IsReadLikeCsv()
    {
        var table = new ExportTable("Sheet1", ["username", "email", "tipo", "server_remoto", "azione"],
            [["user0001", "user0001@example.com", "", "", "set"], ["user0002", "", "", "", "rimuovi"]]);
        var (requests, errors) = TwoFactorInput.ParseXlsx(XlsxWriter.Write([table]));

        Assert.Empty(errors);
        Assert.Equal(2, requests.Count);
        Assert.Equal(("user0001", "user0001@example.com", ChangeAction.Set), (requests[0].Username, requests[0].Email, requests[0].Action));
        Assert.Equal(ChangeAction.Remove, requests[1].Action);
        Assert.Equal(3, requests[1].Row);
    }

    [Fact]
    public void Remove_DisablesEmailTwoFactorFirst_AndRollbackRestoresIt()
    {
        var config = Config(10).Replace("        set email-to \"user0007@example.com\"\n", "        set email-to \"user0007@example.com\"\n        set two-factor email\n", StringComparison.Ordinal);
        var inv = Inventory(config);
        var plan = TwoFactorPlanner.Plan(inv, [new(2, "user0007", null, ChangeAction.Remove)]);
        var bundle = TwoFactorScripts.Generate(plan, inv);

        Assert.Contains("        set two-factor disable\n        unset email-to\n", bundle.Scripts[0].Text, StringComparison.Ordinal);
        Assert.Contains("        set email-to \"user0007@example.com\"\n        set two-factor email\n", bundle.Rollbacks[0].Text, StringComparison.Ordinal);
        var (_, restored) = ApplyAndRollBack(config, bundle);
        Assert.True(restored.SameAs(State(config)));
    }

    [Fact]
    public void Sample_IsValidAndSynthetic()
    {
        var sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "email-2fa.csv"));
        var (requests, errors) = TwoFactorInput.ParseCsv(sample);
        Assert.Empty(errors);
        Assert.NotEmpty(requests);
        Assert.All(requests.Where(r => r.Email is not null), r => Assert.EndsWith("example.com", r.Email!, StringComparison.Ordinal));
    }
}
