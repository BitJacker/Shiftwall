using System.Globalization;
using System.Text;
using Shiftwall.Core.Config;
using Shiftwall.Features.Toolbox.TwoFactor;

namespace Shiftwall.Features.Tests.Toolbox.TwoFactor;

/// <summary>
/// Synthetic configurations for the email 2FA module, generated in code: names are invented, domains are
/// example.com, and every ENC value is the base64 of "Shiftwall synthetic test value - not a secret".
/// </summary>
internal static class TwoFactorFixtures
{
    public const string Enc = "ENC U2hpZnR3YWxsIHN5bnRoZXRpYyB0ZXN0IHZhbHVlIC0gbm90IGEgc2VjcmV0";

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Golden", "TwoFactor");

    /// <summary>
    /// <paramref name="users"/> local users: every 10th is LDAP (server AD-LAB), every 15th RADIUS (RAD-LAB), every
    /// 7th already has an email, user0003 has a FortiToken. Administrators: admin and breakglass (super_admin),
    /// netops (prof_admin, FortiToken). In multi-VDOM mode the users are split between VDOMs root and branch.
    /// </summary>
    public static string Config(int users, bool multiVdom = false, bool emailServer = true, string version = "7.4.9")
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"#config-version=FGTSYN-{version}-FW-build2829-250101:opmode=0:vdom={(multiVdom ? 1 : 0)}:user=admin\n#buildno=2829\n#global_vdom=1\n");
        var g = multiVdom ? "    " : "";
        if (multiVdom) sb.Append("config vdom\nedit root\nnext\nedit branch\nnext\nend\nconfig global\n");
        sb.Append(g).Append("config system global\n").Append(g).Append("    set hostname \"lab-fw\"\n").Append(g).Append("end\n");
        if (emailServer)
        {
            sb.Append(g).Append("config system email-server\n")
              .Append(g).Append("    set server \"smtp.example.com\"\n")
              .Append(g).Append("    set port 465\n")
              .Append(g).Append("    set authenticate enable\n")
              .Append(g).Append("    set username \"fortigate@example.com\"\n")
              .Append(g).Append("    set password ").Append(Enc).Append('\n')
              .Append(g).Append("    set security smtps\n")
              .Append(g).Append("end\n");
        }

        sb.Append(g).Append("config system admin\n");
        Admin(sb, g, "admin", "super_admin", null, null);
        Admin(sb, g, "breakglass", "super_admin", null, null);
        Admin(sb, g, "netops", "prof_admin", "FTKMOB0000000001", "netops@example.com");
        sb.Append(g).Append("end\n");
        if (multiVdom) sb.Append("end\n");

        if (!multiVdom)
        {
            Vdom(sb, "", 1, users);
            return sb.ToString();
        }

        sb.Append("config vdom\nedit root\n");
        Vdom(sb, "", 1, users / 2);
        sb.Append("next\nedit branch\n");
        Vdom(sb, "", (users / 2) + 1, users - (users / 2));
        sb.Append("next\nend\n");
        return sb.ToString();
    }

    private static void Admin(StringBuilder sb, string g, string name, string profile, string? token, string? email)
    {
        sb.Append(g).Append("    edit \"").Append(name).Append("\"\n")
          .Append(g).Append("        set accprofile \"").Append(profile).Append("\"\n")
          .Append(g).Append("        set vdom \"root\"\n");
        if (token is not null) sb.Append(g).Append("        set two-factor fortitoken\n").Append(g).Append("        set fortitoken \"").Append(token).Append("\"\n");
        if (email is not null) sb.Append(g).Append("        set email-to \"").Append(email).Append("\"\n");
        sb.Append(g).Append("        set password ").Append(Enc).Append('\n').Append(g).Append("    next\n");
    }

    private static void Vdom(StringBuilder sb, string g, int first, int count)
    {
        sb.Append(g).Append("config user ldap\n").Append(g).Append("    edit \"AD-LAB\"\n").Append(g).Append("        set server \"192.0.2.10\"\n").Append(g).Append("    next\n").Append(g).Append("end\n");
        sb.Append(g).Append("config user radius\n").Append(g).Append("    edit \"RAD-LAB\"\n").Append(g).Append("        set server \"192.0.2.11\"\n").Append(g).Append("    next\n").Append(g).Append("end\n");
        sb.Append(g).Append("config user local\n");
        for (var n = first; n < first + count; n++)
        {
            var name = Name(n);
            sb.Append(g).Append("    edit \"").Append(name).Append("\"\n");
            if (n % 10 == 0) sb.Append(g).Append("        set type ldap\n").Append(g).Append("        set ldap-server \"AD-LAB\"\n");
            else if (n % 15 == 0) sb.Append(g).Append("        set type radius\n").Append(g).Append("        set radius-server \"RAD-LAB\"\n");
            else sb.Append(g).Append("        set passwd ").Append(Enc).Append('\n');
            if (n == 3) sb.Append(g).Append("        set two-factor fortitoken\n").Append(g).Append("        set fortitoken \"FTKMOB0000000003\"\n");
            if (n % 7 == 0) sb.Append(g).Append("        set email-to \"").Append(name).Append("@example.com\"\n");
            sb.Append(g).Append("    next\n");
        }

        sb.Append(g).Append("end\n");
        sb.Append(g).Append("config user group\n").Append(g).Append("    edit \"VPN-AD\"\n").Append(g).Append("        set member \"AD-LAB\"\n").Append(g).Append("    next\n").Append(g).Append("end\n");
    }

    public static string Name(int n) => "user" + n.ToString("0000", CultureInfo.InvariantCulture);

    public static ConfigDocument Doc(string text) => ConfigParser.Parse(text);

    public static AccountInventory Inventory(string text) => AccountInventory.Read(Doc(text));

    /// <summary>Compares with a reviewed golden file; when missing, writes the actual text next to it and fails.</summary>
    public static void AssertGolden(string file, string actual)
    {
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, Path.GetFileNameWithoutExtension(file) + ".actual" + Path.GetExtension(file)), actual);
            Assert.Fail($"Golden file {file} missing: review the .actual file in the test output folder.");
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    public static string All(ScriptBundle b) => string.Concat(b.Scripts.Select(s => "===== " + s.FileName + "\n" + s.Text).Concat(b.Rollbacks.Select(s => "===== " + s.FileName + "\n" + s.Text)));
}
