using System.IO.Compression;
using System.Xml.Linq;
using Territory.Application.Export;
using Territory.Infrastructure.Export;

namespace Territory.Tests.Application;

public sealed class DocxS13WriterTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "s13");
    private static readonly DocxS13Writer Writer = new(new S13TemplateCatalog(
        Folder, new Dictionary<string, string> { ["it"] = "S-13_I.docx", ["en"] = "S-13_E.docx" }));

    [Fact]
    public void Write_FillsYearMunicipalityAndAssignmentsInTheTemplate()
    {
        var report = Report("it", Territory(1, new DateOnly(2025, 6, 2), Assignment("Mario Rossi", 2025, 11, 10, 2025, 12, 30), Assignment("Laura Verdi", 2026, 1, 25)));

        var pages = Pages(report);

        var page = Assert.Single(pages);
        Assert.Equal("2026 Montefalco", page.Header);
        Assert.Equal(["1", "02/06/25", "Mario Rossi", "Laura Verdi"], page.Texts(0, 0).Where(t => t.Length > 0));
        Assert.Equal(["10/11/25", "30/12/25", "25/01/26"], page.Dates(0));
    }

    [Fact]
    public void Write_UsesTheTemplateOfTheLanguage()
    {
        var italian = Document(Report("it"));
        var english = Document(Report("en"));

        Assert.Contains("REGISTRAZIONE DELLE ASSEGNAZIONI", Text(italian));
        Assert.Contains("TERRITORY ASSIGNMENT RECORD", Text(english));
    }

    [Fact]
    public void Write_UnknownLanguageFallsBackToEnglish() =>
        Assert.Contains("TERRITORY ASSIGNMENT RECORD", Text(Document(Report("xx"))));

    [Fact]
    public void Write_AddsPageWhenTerritoriesDoNotFit()
    {
        var territories = Enumerable.Range(1, 21).Select(n => Territory(n, null)).ToArray();

        var pages = Pages(Report("it", territories));

        Assert.Equal(2, pages.Count);
        Assert.Equal(["21"], pages[1].Texts(0, 0).Where(t => t.Length > 0));
    }

    [Fact]
    public void Write_AddsPageWithSameTerritoriesWhenAssignmentsDoNotFit()
    {
        var many = Enumerable.Range(1, 5).Select(i => Assignment($"Publisher {i}", 2025, 9 + (i % 3), 1)).ToArray();

        var pages = Pages(Report("it", Territory(7, new DateOnly(2025, 5, 5), many), Territory(8, null)));

        Assert.Equal(2, pages.Count);
        Assert.Equal(["7", "05/05/25", "Publisher 1"], pages[0].Texts(0, 0).Where(t => t.Length > 0).Take(3));
        Assert.Equal(["7", "Publisher 5"], pages[1].Texts(0, 0).Where(t => t.Length > 0));
        Assert.Equal(["8"], pages[1].Texts(1, 0).Where(t => t.Length > 0));
        Assert.Equal(pages[0].Header, pages[1].Header);
    }

    [Fact]
    public void Write_EveryMunicipalityStartsItsOwnPage()
    {
        var report = new S13Report("it", 2026, [new S13Municipality("Giano", [Territory(1, null)]), new S13Municipality("Bevagna", [Territory(1, null)])]);

        Assert.Equal(["2026 Giano", "2026 Bevagna"], Pages(report).Select(p => p.Header));
    }

    [Fact]
    public void Write_WithoutTerritoriesProducesTheBlankFormOfTheYear()
    {
        var page = Assert.Single(Pages(new S13Report("it", 2026, [])));

        Assert.Equal("2026", page.Header);
    }

    [Fact]
    public void Write_ShowsTheFormFooterOnEveryPage()
    {
        var sectPr = Document(Report("it", Territory(1, null))).Root!.Element(W + "body")!.Element(W + "sectPr")!;

        Assert.Null(sectPr.Element(W + "titlePg"));
        Assert.Equal("default", (string?)sectPr.Element(W + "footerReference")!.Attribute(W + "type"));
    }

    [Fact]
    public void Write_KeepsTheOtherPartsOfTheTemplate()
    {
        using var output = new MemoryStream();
        Writer.Write(Report("it"), output);
        output.Position = 0;
        using var zip = new ZipArchive(output);

        Assert.Contains(zip.Entries, e => e.FullName == "word/footer1.xml");
        Assert.Contains(zip.Entries, e => e.FullName == "[Content_Types].xml");
    }

    [Fact]
    public void Catalog_RejectsTemplatePaths() =>
        Assert.Throws<InvalidOperationException>(() => new S13TemplateCatalog(Folder, new Dictionary<string, string> { ["it"] = "..\\secret.docx" }));

    [Fact]
    public void Catalog_FailsWhenNoTemplateIsConfigured() =>
        Assert.Throws<InvalidOperationException>(() => new S13TemplateCatalog(Folder, new Dictionary<string, string>()).ReadTemplate("it"));

    private static S13Report Report(string language, params S13Territory[] territories) =>
        new(language, 2026, territories.Length == 0 ? [] : [new S13Municipality("Montefalco", territories)]);

    private static S13Territory Territory(int number, DateOnly? lastReturn, params S13Assignment[] assignments) =>
        new(number, lastReturn, assignments);

    private static S13Assignment Assignment(string name, int year, int month, int day, int? returnYear = null, int? returnMonth = null, int? returnDay = null) =>
        new(name, new DateOnly(year, month, day), returnYear is null ? null : new DateOnly(returnYear.Value, returnMonth!.Value, returnDay!.Value));

    private static XDocument Document(S13Report report)
    {
        using var output = new MemoryStream();
        Writer.Write(report, output);
        output.Position = 0;
        using var zip = new ZipArchive(output);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static string Text(XDocument document) => string.Concat(document.Descendants(W + "t").Select(t => t.Value));

    private static List<PageView> Pages(S13Report report)
    {
        var tables = Document(report).Descendants(W + "tbl").Where(t => t.Parent!.Name == W + "body").ToList();
        return [.. tables.Chunk(2).Select(pair => new PageView(pair[0], pair[1]))];
    }

    private sealed class PageView(XElement header, XElement register)
    {
        private readonly List<XElement> rows = [.. register.Elements(W + "tr").Skip(2)];

        public string Header => CellText(header.Elements(W + "tr").First().Elements(W + "tc").Last());

        /// <summary>Texts of the cells of the top (names) or bottom (dates) row of the territory at the given index.</summary>
        public IEnumerable<string> Texts(int territory, int line) =>
            rows[(2 * territory) + line].Elements(W + "tc").Select(CellText);

        public IEnumerable<string> Dates(int territory) => Texts(territory, 1).Where(t => t.Length > 0);

        private static string CellText(XElement cell) => string.Concat(cell.Descendants(W + "t").Select(t => t.Value));
    }
}
