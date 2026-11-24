using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace DepartmentEmployeeManagement.Api.Services;

public sealed class ExcelWorkbookService
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DocumentRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public byte[] CreateWorkbook(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml", new XDocument(
                new XElement(ContentTypeNs + "Types",
                    new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));

            AddXml(archive, "_rels/.rels", new XDocument(
                new XElement(PackageRelationshipNs + "Relationships",
                    new XElement(PackageRelationshipNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml")))));

            AddXml(archive, "xl/workbook.xml", new XDocument(
                new XElement(SpreadsheetNs + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", DocumentRelationshipNs),
                    new XElement(SpreadsheetNs + "sheets",
                        new XElement(SpreadsheetNs + "sheet", new XAttribute("name", "Employees"), new XAttribute("sheetId", "1"), new XAttribute(DocumentRelationshipNs + "id", "rId1"))))));

            AddXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                new XElement(PackageRelationshipNs + "Relationships",
                    new XElement(PackageRelationshipNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml")))));

            var sheetRows = rows.Select((row, rowIndex) =>
                new XElement(SpreadsheetNs + "row",
                    new XAttribute("r", rowIndex + 1),
                    row.Select((value, columnIndex) =>
                        new XElement(SpreadsheetNs + "c",
                            new XAttribute("r", $"{ColumnName(columnIndex + 1)}{rowIndex + 1}"),
                            new XAttribute("t", "inlineStr"),
                            new XElement(SpreadsheetNs + "is",
                                new XElement(SpreadsheetNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty))))));

            var lastColumn = rows.Count == 0 ? "A" : ColumnName(Math.Max(1, rows.Max(row => row.Count)));
            var lastRow = Math.Max(1, rows.Count);
            AddXml(archive, "xl/worksheets/sheet1.xml", new XDocument(
                new XElement(SpreadsheetNs + "worksheet",
                    new XElement(SpreadsheetNs + "dimension", new XAttribute("ref", $"A1:{lastColumn}{lastRow}")),
                    new XElement(SpreadsheetNs + "sheetViews", new XElement(SpreadsheetNs + "sheetView", new XAttribute("workbookViewId", "0"))),
                    new XElement(SpreadsheetNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "18")),
                    new XElement(SpreadsheetNs + "sheetData", sheetRows),
                    rows.Count > 0 ? new XElement(SpreadsheetNs + "autoFilter", new XAttribute("ref", $"A1:{lastColumn}{lastRow}")) : null)));
        }

        return output.ToArray();
    }

    public ExcelWorksheetData ReadFirstSheet(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("Excel workbook has no first worksheet.");
        if (worksheetEntry.Length > 10 * 1024 * 1024)
            throw new InvalidDataException("The worksheet is too large.");

        var sharedStrings = ReadSharedStrings(archive);
        using var worksheetStream = worksheetEntry.Open();
        var worksheet = XDocument.Load(worksheetStream);
        var sheetRows = worksheet.Root?.Element(SpreadsheetNs + "sheetData")?.Elements(SpreadsheetNs + "row").ToList()
            ?? throw new InvalidDataException("The first worksheet is empty or invalid.");
        if (sheetRows.Count == 0) return new ExcelWorksheetData([], []);

        var headers = ReadCells(sheetRows[0], sharedStrings)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
        var headerNames = headers.Values.ToArray();
        if (headerNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headerNames.Length)
            throw new InvalidDataException("The worksheet contains duplicate column names.");
        var result = new List<ExcelDataRow>();
        foreach (var row in sheetRows.Skip(1))
        {
            var cells = ReadCells(row, sharedStrings);
            if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, header) in headers)
                if (cells.TryGetValue(column, out var value)) values[header] = value.Trim();

            var rowNumber = int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedRow) ? parsedRow : result.Count + 2;
            result.Add(new ExcelDataRow(rowNumber, values));
        }

        return new ExcelWorksheetData(headerNames, result);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        if (entry.Length > 10 * 1024 * 1024) throw new InvalidDataException("The shared strings table is too large.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?.Elements(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToArray() ?? [];
    }

    private static Dictionary<int, string> ReadCells(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var reference = (string?)cell.Attribute("r");
            var column = ColumnNumber(reference);
            if (column < 1) continue;

            var type = (string?)cell.Attribute("t");
            var value = type switch
            {
                "inlineStr" => string.Concat(cell.Element(SpreadsheetNs + "is")?.Descendants(SpreadsheetNs + "t").Select(text => text.Value) ?? []),
                "s" when int.TryParse(cell.Element(SpreadsheetNs + "v")?.Value, out var index) && index >= 0 && index < sharedStrings.Count => sharedStrings[index],
                _ => cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty
            };
            cells[column] = value;
        }
        return cells;
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static int ColumnNumber(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return 0;
        var column = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter))
            column = (column * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        return column;
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }
}

public sealed record ExcelWorksheetData(IReadOnlyList<string> Headers, IReadOnlyList<ExcelDataRow> Rows);
public sealed record ExcelDataRow(int RowNumber, IReadOnlyDictionary<string, string> Values);
