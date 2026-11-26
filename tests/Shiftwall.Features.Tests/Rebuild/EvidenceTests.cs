using System.Globalization;
using System.Text;
using Shiftwall.Features.Migration;
using Shiftwall.Features.Rebuild;

namespace Shiftwall.Features.Tests.Rebuild;

/// <summary>
/// docs/evidence: the scripts and tables generated from the synthetic fixtures, kept in the repository so they can be read
/// without running Shiftwall. The test fails when they are out of date; SHIFTWALL_WRITE_EVIDENCE=1 rewrites them.
/// Secrets are masked: the real files contain the passwords generated at each run.
/// </summary>
public sealed class EvidenceTests
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it");

    private static readonly (string Key, string Title, Func<RebuildRequest> Request)[] Variants =
    [
        ("lan", "internal → lan (switch esistente), wan2 → lan3", () => RebuildFixtures.Office110C()),
        ("recreated", "internal ricreato con lan1 + lan2 (switch hardware), wan2 → lan (resta con lan3)", () => RebuildFixtures.Office110CRecreated()),
        ("split", "internal spezzato: lan1 con l'indirizzo, lan2 senza; wan2 → lan (resta con lan3)", () => RebuildFixtures.Office110CSplit()),
        ("profiles-disabled", "come 'lan', con l'opzione \"Crea la policy disattivata\" per i profili", () => RebuildFixtures.Office110C() with { Options = new RebuildOptions(ManagementInterface: "lan", Profiles: ProfileHandling.DisablePolicy) }),
    ];

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Packages.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Evidence_IsUpToDate()
    {
        var files = Build();
        var root = Path.Combine(RepoRoot(), "docs", "evidence");
        if (Environment.GetEnvironmentVariable("SHIFTWALL_WRITE_EVIDENCE") == "1")
        {
            foreach (var dir in Variants.Select(v => Path.Combine(root, "scripts", v.Key)).Where(Directory.Exists)) Directory.Delete(dir, recursive: true);
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text, new UTF8Encoding(false));
            }

            return;
        }

        foreach (var (path, text) in files)
        {
            var full = Path.Combine(root, path);
            Assert.True(File.Exists(full), $"docs/evidence/{path} is missing: run the tests with SHIFTWALL_WRITE_EVIDENCE=1.");
            Assert.True(File.ReadAllText(full).ReplaceLineEndings("\n") == text, $"docs/evidence/{path} is out of date: run the tests with SHIFTWALL_WRITE_EVIDENCE=1.");
        }
    }

    private static List<(string Path, string Text)> Build()
    {
        var files = new List<(string, string)>();
        var counts = new StringBuilder("# Conteggi sorgente → destinazione\n\nGenerato dai test (`EvidenceTests`) sulla fixture sintetica `110c-5.2-office.fgtcfg` (FortiGate 110C, FortiOS 5.2) verso `40f-8.0-target-synthetic.fgtcfg` (FortiGate 40F, FortiOS 8.0). Ogni riga: oggetti nella sorgente = ricostruiti + convertiti + da fare a mano; nessun oggetto sparisce senza motivo.\n\n");
        var validation = new StringBuilder("# Report di validazione\n\nOgni file generato (GUI e terminale) è riletto riga per riga: ogni `config` deve esistere nello schema CLI 8.0, ogni `set` deve avere un parametro dello schema e un valore valido per il suo tipo. I valori di tipo indirizzo, maschera o intervallo hanno solo il controllo di presenza: il loro formato lo verifica FortiOS (lo schema non lo descrive).\n\n| Variante | File | Righe di comando | `config`/`edit`/`delete` validati | `set` validati sullo schema 8.0 | di cui con controllo del valore | Riferimenti controllati | Errori lint | Errori bloccanti |\n|---|---|---|---|---|---|---|---|---|\n");
        var applied = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var schema = CliSchema.Load("8.0");
        foreach (var (key, title, request) in Variants)
        {
            var r = RebuildEngine.Run(request());
            Assert.False(r.IsBlocked, key);
            foreach (var a in r.Applied)
            {
                if (!applied.TryGetValue(a.RuleId, out var set)) applied[a.RuleId] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(key);
            }

            counts.Append(CultureInfo.InvariantCulture, $"## Variante `{key}`: {title}\n\n| Oggetto | Sorgente | Ricostruiti | Convertiti | A mano | Differenza spiegata |\n|---|---|---|---|---|---|\n");
            foreach (var c in r.Counts)
            {
                var why = new List<string>();
                if (c.Converted > 0) why.Add(Converted(c.Path));
                why.AddRange(r.Manual.Where(m => m.Path == c.Path).Select(m => $"`{m.Object}`: {m.Format(It)}"));
                if (c.Path == "system interface" && c.Source != c.Rebuilt + c.Converted + c.Manual) why.Add("tunnel contati con il loro IPsec");
                counts.Append(CultureInfo.InvariantCulture, $"| `{c.Path}` | {c.Source} | {c.Rebuilt} | {c.Converted} | {c.Manual} | {Cell(why.Count == 0 ? "—" : string.Join("<br>", why))} |\n");
            }

            counts.Append('\n');
            if (key == "profiles-disabled") continue;

            var errors = r.Findings.Count(f => f.Severity == FindingSeverity.Error);
            foreach (var (kind, list) in new[] { ("GUI", r.GuiFiles("110C to 40F", maskSecrets: true)), ("terminale", r.TerminalFiles(maskSecrets: true)) })
            {
                var total = new Tally();
                foreach (var f in list)
                {
                    files.Add(($"scripts/{key}/{(kind == "GUI" ? "gui" : "terminal")}/{f.FileName}", f.Text));
                    total.Add(Validate(f.Text, schema));
                    total.Lint += CliLint.Check(f.Text, schema, allowComments: kind == "GUI").Count(l => l.Severity == LintSeverity.Error);
                }

                validation.Append(CultureInfo.InvariantCulture,
                    $"| `{key}` | {kind} ({list.Count} file) | {total.Commands} | {total.Structure}/{total.Structure} | {total.SetValid}/{total.Sets} ({Pct(total.SetValid, total.Sets)}) | {total.Typed} | {r.ReferencesChecked} | {total.Lint} | {errors} |\n");
            }
        }

        validation.Append("\nI file sono in `docs/evidence/scripts/<variante>/gui` e `.../terminal`, con le password sostituite da `********`.\n");
        files.Add(("counts.md", counts.ToString()));
        files.Add(("validation.md", validation.ToString()));
        files.Add(("rules.md", Rules(applied)));
        return files;
    }

    private static string Converted(string path) => path switch
    {
        "system interface" => "`ssl.root` convertita nel tunnel IPsec dial-up `dialup_vpn`",
        "firewall address" => "indirizzo del pool SSL VPN convertito nell'intervallo del dial-up",
        "firewall policy" => "policy SSL VPN convertita in policy del tunnel dial-up",
        "system virtual-switch" or "system switch-interface" => "switch riportato con il piano degli switch",
        _ when path.StartsWith("vpn ssl", StringComparison.Ordinal) => "impostazioni SSL VPN convertite nel tunnel IPsec dial-up",
        _ => "convertito",
    };

    private static string Cell(string s) => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Pct(int a, int b) => b == 0 ? "—" : (100.0 * a / b).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private sealed class Tally
    {
        public int Commands;
        public int Structure;
        public int Sets;
        public int SetValid;
        public int Typed;
        public int Lint;

        public void Add(Tally o)
        {
            Commands += o.Commands;
            Structure += o.Structure;
            Sets += o.Sets;
            SetValid += o.SetValid;
            Typed += o.Typed;
        }
    }

    /// <summary>Walks a generated file and checks every statement on the 8.0 schema. Throws on anything invalid.</summary>
    private static Tally Validate(string text, CliSchema schema)
    {
        var t = new Tally();
        var stack = new List<SchemaNode>();
        foreach (var raw in text.Split('\n'))
        {
            var s = raw.Trim();
            if (s.Length == 0 || s[0] == '#') continue;
            t.Commands++;
            var tok = CliLint.Tokenize(s);
            switch (tok[0])
            {
                case "config":
                    var name = string.Join(' ', tok.Skip(1));
                    var node = stack.Count == 0 ? schema.Find(name) : stack[^1].Sub.GetValueOrDefault(name);
                    Assert.True(node is not null, $"config {name} is not in the 8.0 schema");
                    stack.Add(node!);
                    t.Structure++;
                    break;
                case "edit" or "delete":
                    Assert.True(stack.Count > 0 && stack[^1].IsTable, $"{s}: not inside a table");
                    t.Structure++;
                    break;
                case "set":
                    t.Sets++;
                    var p = stack[^1].Params.GetValueOrDefault(tok[1]);
                    Assert.True(p is not null, $"set {tok[1]} is not a parameter of the 8.0 schema");
                    var values = tok.Skip(2).ToList();
                    if (values.All(v => v == "********")) values = ["x"];
                    Assert.Null(CliSchema.Check(p!, values));
                    t.SetValid++;
                    if (p!.Type is "option" or "integer" or "string" or "var-string") t.Typed++;
                    break;
                case "end":
                    stack.RemoveAt(stack.Count - 1);
                    break;
                case "next" or "unset":
                    break;
                default:
                    Assert.Fail("Unknown statement: " + s);
                    break;
            }
        }

        Assert.Empty(stack);
        return t;
    }

    private static string Rules(SortedDictionary<string, SortedSet<string>> applied)
    {
        var rules = RuleSet.Load("5.2", "8.0");
        var testDir = Path.Combine(RepoRoot(), "tests", "Shiftwall.Features.Tests");
        var tests = Directory.GetFiles(Path.Combine(testDir, "Rebuild"), "*.cs").Where(f => !f.EndsWith("EvidenceTests.cs", StringComparison.Ordinal))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f))).ToList();
        var goldens = Directory.GetFiles(Path.Combine(testDir, "Golden", "Rebuild"))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f).ReplaceLineEndings("\n"))).ToList();
        var sb = new StringBuilder("# Regole di traduzione usate negli script di prova\n\nOgni regola applicata almeno una volta nelle varianti della fixture 110C → 40F, con la fonte ufficiale e i test che la coprono. Il golden test confronta riga per riga l'uscita completa, comprese le regole applicate (sezione `#### RULES`).\n\n| Regola | Tipo | Dove | Cosa fa | Fonti | Varianti | Test |\n|---|---|---|---|---|---|---|\n");
        foreach (var (id, variants) in applied)
        {
            var r = rules.Rules.First(x => x.Id == id);
            var covered = tests.Where(t => t.Text.Contains('"' + id + '"', StringComparison.Ordinal)).Select(t => t.Name)
                .Concat(goldens.Where(g => g.Text.Contains("\n" + id + " ", StringComparison.Ordinal)).Select(g => "golden " + g.Name)).ToList();
            Assert.NotEmpty(covered);
            var src = string.Join("<br>", r.Sources.Select(s => $"[{s.Title}]({s.Url})"));
            sb.Append(CultureInfo.InvariantCulture, $"| {id} | {r.Kind} | `{r.Path}` {r.Param} | {Cell(r.Note)} | {Cell(src)} | {string.Join(", ", variants)} | {string.Join("<br>", covered)} |\n");
        }

        return sb.ToString();
    }
}
