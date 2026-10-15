using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Territory.Application.Export;

namespace Territory.Infrastructure.Export;

/// <summary>Printable Word register: title, applied filters, then one table per municipality.</summary>
public sealed class DocxRegisterWriter : IRegisterWriter
{
    // Font sizes are in half-points.
    private const string TitleSize = "36";
    private const string HeadingSize = "28";
    private const uint BorderSize = 4;

    public string Format => "docx";

    public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public void Write(RegisterReport report, Stream output)
    {
        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new Body();
        AppendHeader(body, report);
        foreach (var municipality in report.ByMunicipality())
        {
            body.Append(Paragraph(municipality.Key, bold: true, HeadingSize));
            body.Append(RegisterTable(report.Texts, municipality));
        }

        mainPart.Document = new Document(body);
        mainPart.Document.Save();
    }

    private static void AppendHeader(Body body, RegisterReport report)
    {
        var texts = report.Texts;
        body.Append(Paragraph(texts.Title, bold: true, TitleSize));
        body.Append(Paragraph(texts.Scope(report.MunicipalityName)));
        body.Append(Paragraph(texts.Period(report.From, report.To)));
        body.Append(Paragraph(texts.Generated(report.GeneratedOn)));
    }

    private static Table RegisterTable(RegisterTexts texts, IEnumerable<RegisterTerritory> territories)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = BorderSize },
                new BottomBorder { Val = BorderValues.Single, Size = BorderSize },
                new LeftBorder { Val = BorderValues.Single, Size = BorderSize },
                new RightBorder { Val = BorderValues.Single, Size = BorderSize },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = BorderSize },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = BorderSize })));
        table.Append(Row(bold: true, texts.Number, texts.Territory, texts.Publisher, texts.AssignedOn, texts.ReturnedOn));
        table.Append(territories.SelectMany(t => TerritoryRows(texts, t)));
        return table;
    }

    private static IEnumerable<TableRow> TerritoryRows(RegisterTexts texts, RegisterTerritory territory)
    {
        var number = territory.Number.ToString(texts.Culture);
        if (territory.Entries.Count == 0)
        {
            yield return Row(bold: false, number, territory.Name, texts.NoAssignments, string.Empty, string.Empty);
            yield break;
        }

        foreach (var entry in territory.Entries)
        {
            yield return Row(bold: false, number, territory.Name, entry.PublisherName, texts.Date(entry.AssignedOn), texts.Date(entry.ReturnedOn));
        }
    }

    private static TableRow Row(bool bold, params string[] cells)
    {
        var row = new TableRow();
        if (bold)
        {
            row.Append(new TableRowProperties(new TableHeader()));
        }

        row.Append(cells.Select(text => new TableCell(Paragraph(text, bold))));
        return row;
    }

    private static Paragraph Paragraph(string text, bool bold = false, string? size = null)
    {
        var properties = new RunProperties();
        if (bold)
        {
            properties.Append(new Bold());
        }

        if (size is not null)
        {
            properties.Append(new FontSize { Val = size });
        }

        return new Paragraph(new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}
