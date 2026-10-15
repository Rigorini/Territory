using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Territory.Application.Domain;
using Territory.Application.Export;
using Territory.Infrastructure.Export;

namespace Territory.Tests.Application;

public sealed class RegisterWritersTests
{
    private const string FormulaName = "=HYPERLINK(\"http://evil\")";

    private static readonly DateOnly Day = new(2025, 3, 10);

    private static RegisterReport SampleReport(RegisterTexts texts) => new(
        texts,
        Day,
        null,
        new DateOnly(2025, 1, 1),
        null,
        [
            new RegisterTerritory("Giano", 1, "Centro", "Rossi Mario", Day,
            [
                new RegisterEntry("Bianchi Anna", new DateOnly(2025, 1, 5), new DateOnly(2025, 2, 1), ReturnedBy.Publisher),
                new RegisterEntry("Rossi Mario", Day, null, null)
            ]),
            new RegisterTerritory("Giano", 2, FormulaName, null, null, []),
            new RegisterTerritory("Montecchio", 1, "Borgo", null, new DateOnly(2024, 12, 1), [])
        ]);

    [Fact]
    public void Xlsx_HasStatusAndRegisterSheets()
    {
        var writer = new XlsxRegisterWriter();
        using var stream = new MemoryStream();

        writer.Write(SampleReport(RegisterTexts.Italian), stream);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var status = workbook.Worksheet("Stato");
        Assert.Equal(["Comune", "N°", "Territorio", "Stato", "Proclamatore", "Dal"], HeaderOf(status, 6));
        Assert.Equal("Assegnato", status.Cell(2, 4).GetString());
        Assert.Equal("Rossi Mario", status.Cell(2, 5).GetString());
        Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), status.Cell(2, 6).GetDateTime());
        Assert.Equal(XlsxRegisterWriter.DayNumberFormat, status.Cell(2, 6).Style.NumberFormat.Format);
        Assert.Equal("Disponibile", status.Cell(3, 4).GetString());
        Assert.True(status.Cell(3, 6).IsEmpty());
        Assert.Equal(4, status.LastRowUsed()!.RowNumber());

        var register = workbook.Worksheet("Registro");
        Assert.Equal(5, register.LastRowUsed()!.RowNumber());
        Assert.Equal("Bianchi Anna", register.Cell(2, 4).GetString());
        Assert.Equal(new DateTime(2025, 2, 1), register.Cell(2, 6).GetDateTime());
        Assert.Equal(XlsxRegisterWriter.DayNumberFormat, register.Cell(2, 5).Style.NumberFormat.Format);
        Assert.Equal("Proclamatore", register.Cell(2, 7).GetString());
        Assert.True(register.Cell(3, 6).IsEmpty());
        Assert.True(register.Cell(4, 4).IsEmpty());
        Assert.Equal("Borgo", register.Cell(5, 3).GetString());
    }

    [Fact]
    public void Xlsx_WritesFormulaLikeNamesAsText()
    {
        using var stream = new MemoryStream();

        new XlsxRegisterWriter().Write(SampleReport(RegisterTexts.Italian), stream);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var cell = workbook.Worksheet("Stato").Cell(3, 3);
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(FormulaName, cell.GetString());
    }

    [Fact]
    public void Docx_HasHeaderAndOneTablePerMunicipality()
    {
        var writer = new DocxRegisterWriter();
        using var stream = new MemoryStream();

        writer.Write(SampleReport(RegisterTexts.English), stream);

        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Equal(
            ["Territory register", "All municipalities", "Period: from 01/01/2025", "Generated on 10/03/2025", "Giano", "Montecchio"],
            paragraphs);
        var tables = body.Elements<Table>().ToList();
        Assert.Equal(2, tables.Count);
        Assert.Equal(
            [
                ["No.", "Territory", "Publisher", "Assigned on", "Returned on"],
                ["1", "Centro", "Bianchi Anna", "05/01/2025", "01/02/2025"],
                ["1", "Centro", "Rossi Mario", "10/03/2025", ""],
                ["2", FormulaName, "No assignments in the period", "", ""]
            ],
            CellsOf(tables[0]));
        Assert.Equal(2, CellsOf(tables[1]).Count);
    }

    [Fact]
    public void Writers_DeclareFormatAndContentType()
    {
        Assert.Equal(("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            (new XlsxRegisterWriter().Format, new XlsxRegisterWriter().ContentType));
        Assert.Equal(("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            (new DocxRegisterWriter().Format, new DocxRegisterWriter().ContentType));
    }

    private static string[] HeaderOf(IXLWorksheet sheet, int columns) =>
        [.. Enumerable.Range(1, columns).Select(c => sheet.Cell(1, c).GetString())];

    private static List<string[]> CellsOf(Table table) =>
        [.. table.Elements<TableRow>().Select(r => r.Elements<TableCell>().Select(c => c.InnerText).ToArray())];
}
