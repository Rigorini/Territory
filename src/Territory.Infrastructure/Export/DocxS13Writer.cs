using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Territory.Application.Common;
using Territory.Application.Export;

namespace Territory.Infrastructure.Export;

/// <summary>
/// Fills the official S-13 Word template: one page per municipality (and per block of territories / of assignments that
/// does not fit), repeating the template page and writing the cell texts into its table.
/// </summary>
public sealed class DocxS13Writer(S13TemplateCatalog catalog) : IS13Writer
{
    private const string DayFormat = "dd'/'MM'/'yy";
    private const int HeaderRows = 2;
    private const int LeadingColumns = 2;
    private const string DocumentEntry = "word/document.xml";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XName Paragraph = W + "p";
    private static readonly XName Table = W + "tbl";
    private static readonly XName Row = W + "tr";
    private static readonly XName Cell = W + "tc";
    private static readonly XName ParagraphProperties = W + "pPr";
    private static readonly XName RunProperties = W + "rPr";

    /// <summary>Territories of a sheet and the slice of their assignments shown on it.</summary>
    internal sealed record Page(string Municipality, IReadOnlyList<S13Territory> Territories, int FirstAssignment, bool FirstOfBlock);

    public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public void Write(S13Report report, Stream output)
    {
        using var template = new MemoryStream(catalog.ReadTemplate(report.Language));
        using var source = new ZipArchive(template, ZipArchiveMode.Read);
        using var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var entry in source.Entries)
        {
            if (entry.FullName == DocumentEntry)
            {
                WriteDocument(entry, target, report);
            }
            else
            {
                CopyEntry(entry, target);
            }
        }
    }

    private static void CopyEntry(ZipArchiveEntry entry, ZipArchive target)
    {
        using var input = entry.Open();
        using var copy = target.CreateEntry(entry.FullName).Open();
        input.CopyTo(copy);
    }

    private static void WriteDocument(ZipArchiveEntry entry, ZipArchive target, S13Report report)
    {
        XDocument document;
        using (var input = entry.Open())
        {
            document = XDocument.Load(input);
        }

        var body = document.Root!.Element(W + "body")!;
        var sectPr = body.Elements(W + "sectPr").Last();
        var pageTemplate = body.Elements().Where(e => e != sectPr).ToList();
        var layout = PageLayout.Of(pageTemplate);
        body.RemoveNodes();
        var pages = Paginate(report, layout.RowsPerPage, layout.Columns);
        for (var i = 0; i < pages.Count; i++)
        {
            body.Add(BuildPage(pageTemplate, pages[i], report.ServiceYear, i > 0));
        }

        ShowFooterOnEveryPage(sectPr);
        body.Add(sectPr);
        using var copy = target.CreateEntry(entry.FullName).Open();
        document.Save(copy);
    }

    /// <summary>Territories are split in blocks of rowsPerPage; a block needs more sheets when some territory has more assignments than columns.</summary>
    internal static List<Page> Paginate(S13Report report, int rowsPerPage, int columns)
    {
        var pages = new List<Page>();
        foreach (var municipality in report.Municipalities)
        {
            foreach (var block in municipality.Territories.Chunk(rowsPerPage))
            {
                var sheets = Math.Max(1, (block.Max(t => t.Assignments.Count) + columns - 1) / columns);
                for (var sheet = 0; sheet < sheets; sheet++)
                {
                    pages.Add(new Page(municipality.Name, block, sheet * columns, sheet == 0));
                }
            }
        }

        // An empty register still yields the blank form of the service year.
        return pages.Count > 0 ? pages : [new Page(string.Empty, [], 0, true)];
    }

    private static List<XElement> BuildPage(List<XElement> pageTemplate, Page page, int serviceYear, bool startsNewPage)
    {
        var elements = pageTemplate.Select(e => new XElement(e)).ToList();
        foreach (var element in elements)
        {
            RemoveVolatileMarkup(element);
        }

        if (startsNewPage)
        {
            AddPageBreakBefore(elements[0]);
        }

        var tables = elements.Where(e => e.Name == Table).ToList();
        var yearCell = tables[0].Elements(Row).First().Elements(Cell).Last();
        SetText(yearCell, $"{serviceYear.ToString(CultureInfo.InvariantCulture)} {page.Municipality}".TrimEnd());
        FillRows(tables[1], page);
        return elements;
    }

    /// <summary>Paragraph ids must be unique in the document, so copies drop them; Word regenerates them.</summary>
    private static void RemoveVolatileMarkup(XElement root)
    {
        root.Descendants(W + "lastRenderedPageBreak").ToList().ForEach(e => e.Remove());
        foreach (var element in root.DescendantsAndSelf())
        {
            element.Attribute(W14 + "paraId")?.Remove();
            element.Attribute(W14 + "textId")?.Remove();
        }
    }

    private static void AddPageBreakBefore(XElement firstParagraph)
    {
        var properties = firstParagraph.Element(ParagraphProperties);
        if (properties is null)
        {
            properties = new XElement(ParagraphProperties);
            firstParagraph.AddFirst(properties);
        }

        var pageBreak = new XElement(W + "pageBreakBefore");
        var anchor = properties.Elements().LastOrDefault(e => e.Name.LocalName is "pStyle" or "keepNext" or "keepLines");
        if (anchor is null)
        {
            properties.AddFirst(pageBreak);
        }
        else
        {
            anchor.AddAfterSelf(pageBreak);
        }
    }

    private static void FillRows(XElement table, Page page)
    {
        var rows = table.Elements(Row).Skip(HeaderRows).ToList();
        foreach (var cell in rows.SelectMany(r => r.Elements(Cell)))
        {
            SetText(cell, string.Empty);
        }

        for (var i = 0; i < page.Territories.Count; i++)
        {
            var territory = page.Territories[i];
            var top = rows[2 * i].Elements(Cell).ToList();
            var bottom = rows[(2 * i) + 1].Elements(Cell).ToList();
            SetText(top[0], territory.Number.ToString(CultureInfo.InvariantCulture));
            if (page.FirstOfBlock)
            {
                SetText(top[1], Day(territory.LastReturnedBefore));
            }

            var shown = territory.Assignments.Skip(page.FirstAssignment).Take(top.Count - LeadingColumns).ToList();
            for (var column = 0; column < shown.Count; column++)
            {
                SetText(top[LeadingColumns + column], shown[column].PublisherName);
                SetText(bottom[LeadingColumns + (2 * column)], Day(shown[column].AssignedOn));
                SetText(bottom[LeadingColumns + (2 * column) + 1], Day(shown[column].ReturnedOn));
            }
        }
    }

    private static string Day(DateOnly? day) => day?.ToString(DayFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Replaces the text of the cell keeping the character format of its paragraph mark.</summary>
    private static void SetText(XElement cell, string text)
    {
        var paragraph = cell.Elements(Paragraph).First();
        paragraph.Elements().Where(e => e.Name != ParagraphProperties).ToList().ForEach(e => e.Remove());
        if (text.Length == 0)
        {
            return;
        }

        var format = paragraph.Element(ParagraphProperties)?.Element(RunProperties);
        paragraph.Add(new XElement(
            W + "r",
            format is null ? null : new XElement(format),
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }

    /// <summary>The template shows its footer (form code) on the first page only: every generated page needs it.</summary>
    private static void ShowFooterOnEveryPage(XElement sectPr)
    {
        var references = sectPr.Elements(W + "footerReference").ToList();
        if (references.Any(r => (string?)r.Attribute(W + "type") == "default"))
        {
            return;
        }

        references.FirstOrDefault(r => (string?)r.Attribute(W + "type") == "first")?.SetAttributeValue(W + "type", "default");
        sectPr.Element(W + "titlePg")?.Remove();
    }

    /// <summary>Capacity of the template page, read from its table so it follows the official form.</summary>
    private sealed record PageLayout(int RowsPerPage, int Columns)
    {
        public static PageLayout Of(List<XElement> pageTemplate)
        {
            var tables = pageTemplate.Where(e => e.Name == Table).ToList();
            if (tables.Count != 2)
            {
                throw new InvalidOperationException("The S-13 template must contain the header table and the register table.");
            }

            var rows = tables[1].Elements(Row).ToList();
            var columns = rows.Count > HeaderRows ? rows[HeaderRows].Elements(Cell).Count() - LeadingColumns : 0;
            var rowsPerPage = (rows.Count - HeaderRows) / 2;
            if (columns < 1 || rowsPerPage < 1)
            {
                throw new InvalidOperationException("The S-13 template register table has an unexpected layout.");
            }

            return new PageLayout(rowsPerPage, columns);
        }
    }
}

/// <summary>Maps each language to its S-13 template file inside the configured folder.</summary>
public sealed class S13TemplateCatalog
{
    private readonly string folder;
    private readonly IReadOnlyDictionary<string, string> templates;

    public S13TemplateCatalog(string folder, IReadOnlyDictionary<string, string> templates)
    {
        foreach (var file in templates.Values)
        {
            if (file != Path.GetFileName(file))
            {
                throw new InvalidOperationException($"S13 template '{file}' must be a file name without path.");
            }
        }

        this.folder = folder;
        this.templates = new Dictionary<string, string>(templates, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The template of the language, or the English one when the language has none.</summary>
    public byte[] ReadTemplate(string language)
    {
        if (!templates.TryGetValue(language, out var file) && !templates.TryGetValue(Languages.English, out file))
        {
            throw new InvalidOperationException($"No S13 template configured for language '{language}'.");
        }

        // Read-share allows exporting while someone has the template open in Word.
        using var stream = new FileStream(Path.Combine(folder, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
