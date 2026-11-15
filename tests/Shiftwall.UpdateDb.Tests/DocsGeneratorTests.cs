using Shiftwall.Core.Data;
using Shiftwall.UpdateDb;

namespace Shiftwall.UpdateDb.Tests;

public sealed class DocsGeneratorTests
{
    [Fact]
    public void ReplaceTable_ReplacesOnlyTheTextBetweenMarkers()
    {
        var doc = $"# Title\n\nBefore\n{DocsGenerator.TableStart}\nold table\n{DocsGenerator.TableEnd}\nAfter\n";

        var result = DocsGenerator.ReplaceTable(doc, "| a |\n");

        Assert.Equal($"# Title\n\nBefore\n{DocsGenerator.TableStart}\n| a |\n{DocsGenerator.TableEnd}\nAfter\n", result);
    }

    [Fact]
    public void ReplaceTable_KeepsWindowsLineEndings()
    {
        var doc = $"x\r\n{DocsGenerator.TableStart}\r\n{DocsGenerator.TableEnd}\r\n";

        var result = DocsGenerator.ReplaceTable(doc, "| a |\n| b |\n");

        Assert.DoesNotContain("\n", result.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("| a |\r\n| b |\r\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceTable_WithoutMarkers_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DocsGenerator.ReplaceTable("no markers", "| a |"));
    }

    [Fact]
    public void ModelsTable_ListsEveryModelOfTheBundledDatabase()
    {
        var db = ModelDatabase.LoadEmbedded();

        var en = DocsGenerator.ModelsTable(db, "docs/SOURCES.md", italian: false);
        var it = DocsGenerator.ModelsTable(db, "docs/SOURCES.md", italian: true);

        foreach (var m in db.Models)
        {
            Assert.Contains($"| {m.Id} |", en, StringComparison.Ordinal);
            Assert.Contains($"| {m.Id} |", it, StringComparison.Ordinal);
        }

        Assert.Contains("(docs/SOURCES.md)", en, StringComparison.Ordinal);
        Assert.Contains("| Modello |", it, StringComparison.Ordinal);
    }
}
