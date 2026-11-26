using System.IO.Compression;
using System.Xml;

namespace Shiftwall.Features.Toolbox.Import;

/// <summary>
/// Reads the first worksheet of an .xlsx file (Office Open XML, ECMA-376) as rows of text cells. Values only:
/// formulas are read as their cached result, styles are ignored. No external library; inputs are size-limited.
/// </summary>
public static class XlsxReader
{
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const long MaxPartBytes = 128L * 1024 * 1024;
    public const int MaxRows = 200_000;
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static List<List<string>> Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("The Excel file is too large.");
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            var doc = Load(ss);
            foreach (XmlElement si in doc.GetElementsByTagName("si", Main)) shared.Add(Text(si));
        }

        var sheetPath = FirstSheetPath(zip) ?? "xl/worksheets/sheet1.xml";
        var sheetEntry = zip.GetEntry(sheetPath) ?? throw new InvalidDataException("The Excel file has no worksheet.");
        var sheet = Load(sheetEntry);
        var rows = new List<List<string>>();
        foreach (XmlElement row in sheet.GetElementsByTagName("row", Main))
        {
            if (rows.Count >= MaxRows) throw new InvalidDataException("The Excel file has too many rows.");
            var cells = new List<string>();
            foreach (XmlElement c in row.GetElementsByTagName("c", Main))
            {
                var index = ColumnIndex(c.GetAttribute("r"));
                if (index < 0) index = cells.Count;
                if (index > 1000) continue;
                while (cells.Count < index) cells.Add("");
                cells.Add(CellText(c, shared));
            }

            var r = int.TryParse(row.GetAttribute("r"), out var rn) ? rn - 1 : rows.Count;
            while (rows.Count < r && rows.Count < MaxRows) rows.Add([]);
            rows.Add(cells);
        }

        return rows;
    }

    private static string? FirstSheetPath(ZipArchive zip)
    {
        if (zip.GetEntry("xl/workbook.xml") is not { } wb || zip.GetEntry("xl/_rels/workbook.xml.rels") is not { } rels) return null;
        var sheet = Load(wb).GetElementsByTagName("sheet", Main).OfType<XmlElement>().FirstOrDefault();
        var id = sheet?.GetAttribute("id", Rel);
        if (string.IsNullOrEmpty(id)) return null;
        foreach (XmlElement r in Load(rels).GetElementsByTagName("Relationship"))
        {
            if (r.GetAttribute("Id") != id) continue;
            var target = r.GetAttribute("Target").TrimStart('/');
            return target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
        }

        return null;
    }

    private static string CellText(XmlElement c, List<string> shared)
    {
        var type = c.GetAttribute("t");
        if (type == "inlineStr") return c.GetElementsByTagName("is", Main).OfType<XmlElement>().FirstOrDefault() is { } inline ? Text(inline) : "";
        var v = c.GetElementsByTagName("v", Main).OfType<XmlElement>().FirstOrDefault()?.InnerText ?? "";
        return type == "s" && int.TryParse(v, out var i) && i >= 0 && i < shared.Count ? shared[i] : v;
    }

    /// <summary>Concatenates the &lt;t&gt; runs (rich text), skipping phonetic runs.</summary>
    private static string Text(XmlElement e) =>
        string.Concat(e.GetElementsByTagName("t", Main).OfType<XmlElement>().Where(t => t.ParentNode?.LocalName != "rPh").Select(t => t.InnerText));

    private static int ColumnIndex(string reference)
    {
        var n = 0;
        var letters = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                n = (n * 26) + (ch - 'A' + 1);
                letters++;
            }
            else
            {
                break;
            }
        }

        return letters == 0 ? -1 : n - 1;
    }

    private static XmlDocument Load(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes) throw new InvalidDataException("The Excel file is too large.");
        var doc = new XmlDocument { XmlResolver = null };
        using var s = entry.Open();
        using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes });
        doc.Load(reader);
        return doc;
    }
}
