using System.IO.Compression;
using System.Text;

namespace Shiftwall.Features.Toolbox.Export;

/// <summary>A sheet of exported data.</summary>
public sealed record ExportTable(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>CSV (RFC 4180) writer with protection against spreadsheet formula injection.</summary>
public static class CsvWriter
{
    /// <summary>
    /// Writes one table as CSV. Cells starting with = + - @ tab or CR are prefixed with an apostrophe so that a
    /// spreadsheet does not run them as formulas (OWASP "CSV Injection").
    /// </summary>
    public static string Write(ExportTable table, char separator = ',')
    {
        var sb = new StringBuilder();
        Line(sb, table.Headers, separator);
        foreach (var row in table.Rows) Line(sb, row, separator);
        return sb.ToString();
    }

    /// <summary>UTF-8 with BOM, so Excel detects the encoding.</summary>
    public static byte[] ToBytes(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    private static void Line(StringBuilder sb, IReadOnlyList<string> cells, char separator)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(Cell(cells[i] ?? "", separator));
        }

        sb.Append("\r\n");
    }

    internal static string Cell(string value, char separator)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        var needsQuotes = value.Contains(separator) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}

/// <summary>
/// Minimal Office Open XML (ECMA-376) workbook writer: one sheet per table, bold header row, frozen header,
/// auto-filter. Cells are inline strings, so nothing is ever evaluated as a formula.
/// </summary>
public static class XlsxWriter
{
    public static byte[] Write(IReadOnlyList<ExportTable> tables)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var names = SheetNames(tables);
            Entry(zip, "[Content_Types].xml", ContentTypes(tables.Count));
            Entry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Entry(zip, "xl/workbook.xml", Workbook(names));
            Entry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(tables.Count));
            Entry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>");
            for (var i = 0; i < tables.Count; i++) Entry(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(tables[i]));
        }

        return ms.ToArray();
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes);
    }

    private static string ContentTypes(int sheets)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (var i = 1; i <= sheets; i++)
        {
            sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i)
              .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (var i = 0; i < names.Count; i++)
        {
            sb.Append("<sheet name=\"").Append(Escape(names[i])).Append("\" sheetId=\"").Append(i + 1).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
        }

        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRels(int sheets)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var i = 1; i <= sheets; i++)
        {
            sb.Append("<Relationship Id=\"rId").Append(i).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
              .Append(i).Append(".xml\"/>");
        }

        sb.Append("<Relationship Id=\"rId").Append(sheets + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static string Sheet(ExportTable t)
    {
        var cols = t.Headers.Count;
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (cols > 0)
        {
            sb.Append("<cols>");
            for (var c = 0; c < cols; c++)
            {
                var width = Math.Clamp(Math.Max(t.Headers[c].Length, t.Rows.Take(200).Select(r => c < r.Count ? (r[c] ?? "").Length : 0).DefaultIfEmpty(0).Max()) + 2, 8, 60);
                sb.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1).Append("\" width=\"").Append(width).Append("\" customWidth=\"1\"/>");
            }

            sb.Append("</cols>");
        }

        sb.Append("<sheetData>");
        Row(sb, 1, t.Headers, 1);
        for (var r = 0; r < t.Rows.Count; r++) Row(sb, r + 2, t.Rows[r], 2);
        sb.Append("</sheetData>");
        if (cols > 0) sb.Append("<autoFilter ref=\"A1:").Append(Column(cols - 1)).Append(t.Rows.Count + 1).Append("\"/>");
        return sb.Append("</worksheet>").ToString();
    }

    private static void Row(StringBuilder sb, int index, IReadOnlyList<string> cells, int style)
    {
        sb.Append("<row r=\"").Append(index).Append("\">");
        for (var c = 0; c < cells.Count; c++)
        {
            var v = cells[c] ?? "";
            if (v.Length > 32767) v = v[..32767];
            sb.Append("<c r=\"").Append(Column(c)).Append(index).Append("\" t=\"inlineStr\" s=\"").Append(style).Append("\"><is><t xml:space=\"preserve\">")
              .Append(Escape(v)).Append("</t></is></c>");
        }

        sb.Append("</row>");
    }

    internal static string Column(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            var m = (index - 1) % 26;
            s = (char)('A' + m) + s;
            index = (index - 1) / 26;
        }

        return s;
    }

    private static List<string> SheetNames(IReadOnlyList<ExportTable> tables)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var t in tables)
        {
            var name = new string([.. t.Name.Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\'))]).Trim('\'');
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name[..31];
            var candidate = name;
            for (var n = 2; !used.Add(candidate); n++) candidate = (name.Length > 28 ? name[..28] : name) + " " + n;
            list.Add(candidate);
        }

        return list;
    }

    /// <summary>XML text escape that also drops characters not allowed in XML 1.0.</summary>
    internal static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t' or '\n' or '\r': sb.Append(ch); break;
                case < ' ': break;
                case '\uFFFE' or '\uFFFF': break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }
}
