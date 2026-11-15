using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Shiftwall.Architecture.Tests;

/// <summary>
/// Privacy guard: no code shipped in Shiftwall.exe may open network connections, except the opt-in
/// LAN module (src/Shiftwall.Lan, added in a later phase). tools/ and tests/ are not shipped and not checked.
/// </summary>
public sealed partial class NoNetworkTests
{
    /// <summary>Only this project may use networking APIs.</summary>
    private const string LanProject = "Shiftwall.Lan";

    private static readonly string[] ForbiddenNamespaces = ["System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets", "System.Net.Mail", "System.Net.NetworkInformation", "System.Net.Quic"];

    private static readonly string[] ForbiddenTypes =
    [
        "System.Net.WebClient", "System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.HttpListener",
        "System.Net.Dns", "System.Net.FtpWebRequest", "System.Net.ServicePointManager",
    ];

    [GeneratedRegex(@"\b(HttpClient|HttpMessageHandler|SocketsHttpHandler|HttpClientHandler|WebClient|WebRequest|HttpWebRequest|HttpListener|TcpClient|TcpListener|UdpClient|Socket|ClientWebSocket|SmtpClient|Dns\.|Ping\b|QuicConnection)\b|System\.Net\.(Http|Sockets|WebSockets|Mail|NetworkInformation|Quic)")]
    private static partial Regex NetworkApi();

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static TheoryData<string> ShippedProjects() =>
    [
        .. Directory.GetDirectories(Path.Combine(RepoRoot(), "src"))
            .Select(Path.GetFileName)
            .Where(n => n is not null && n != LanProject)
            .Select(n => n!),
    ];

    [Theory]
    [MemberData(nameof(ShippedProjects))]
    public void SourceCode_UsesNoNetworkApi(string project)
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && NetworkApi().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(RepoRoot(), x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Network API used outside src/Shiftwall.Lan:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void CompiledCore_ReferencesNoNetworkType()
    {
        var path = typeof(Shiftwall.Core.Data.ModelDatabase).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();
        var referenced = md.TypeReferences
            .Select(h => md.GetTypeReference(h))
            .Select(t => $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}")
            .ToList();
        var bad = referenced.Where(t => ForbiddenTypes.Contains(t) || ForbiddenNamespaces.Any(ns => t.StartsWith(ns + ".", StringComparison.Ordinal))).ToList();
        Assert.True(bad.Count == 0, "Shiftwall.Core references network types: " + string.Join(", ", bad));

        var assemblyRefs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
        Assert.DoesNotContain("System.Net.Http", assemblyRefs);
        Assert.DoesNotContain("System.Net.Sockets", assemblyRefs);
    }

    [Fact]
    public void Detector_CatchesKnownPatterns()
    {
        Assert.Matches(NetworkApi(), "using var client = new HttpClient();");
        Assert.Matches(NetworkApi(), "using System.Net.Sockets;");
        Assert.DoesNotMatch(NetworkApi(), "var socketCount = 3; // the word socketCount is fine");
    }
}
