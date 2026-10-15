using ClosedXML.Excel;
using Territory.Application.Export;

namespace Territory.Infrastructure.Export;

/// <summary>
/// Excel register: a "status" sheet (one row per territory) and a "register" sheet (one row per assignment).
/// Every text is written as a string cell, never as a formula, so names cannot inject formulas.
/// </summary>
public sealed class XlsxRegisterWriter : IRegisterWriter
{
    // Explicit dd/mm/yyyy with escaped separators: the built-in short date (id 14) follows the reader's locale
    // and would show 03/10/2025 as March 10th in a US Excel.
    public const string DayNumberFormat = @"dd\/mm\/yyyy";

    public string Format => "xlsx";

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public void Write(RegisterReport report, Stream output)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = report.Texts.Title;
        WriteStatus(workbook.Worksheets.Add(report.Texts.StatusSheet), report);
        WriteRegister(workbook.Worksheets.Add(report.Texts.RegisterSheet), report);
        workbook.SaveAs(output);
    }

    private static void WriteStatus(IXLWorksheet sheet, RegisterReport report)
    {
        var texts = report.Texts;
        WriteHeader(sheet, [(texts.Municipality, 24), (texts.Number, 8), (texts.Territory, 32), (texts.Status, 14), (texts.Publisher, 28), (texts.Since, 14)]);
        var row = 2;
        foreach (var territory in report.Territories)
        {
            WriteTerritory(sheet.Row(row), territory);
            sheet.Cell(row, 4).SetValue(texts.StatusOf(territory));
            sheet.Cell(row, 5).SetValue(territory.CurrentPublisher ?? string.Empty);
            SetDate(sheet.Cell(row, 6), territory.Since);
            row++;
        }

        Finish(sheet, row - 1, 6);
    }

    private static void WriteRegister(IXLWorksheet sheet, RegisterReport report)
    {
        var texts = report.Texts;
        WriteHeader(sheet, [(texts.Municipality, 24), (texts.Number, 8), (texts.Territory, 32), (texts.Publisher, 28), (texts.AssignedOn, 16), (texts.ReturnedOn, 16), (texts.ReturnedBy, 18)]);
        var row = 2;
        foreach (var (territory, entry) in report.Rows())
        {
            WriteTerritory(sheet.Row(row), territory);
            if (entry is not null)
            {
                sheet.Cell(row, 4).SetValue(entry.PublisherName);
                SetDate(sheet.Cell(row, 5), entry.AssignedOn);
                SetDate(sheet.Cell(row, 6), entry.ReturnedOn);
                sheet.Cell(row, 7).SetValue(texts.ReturnedByText(entry.ReturnedBy, entry.ReturnedByName));
            }

            row++;
        }

        Finish(sheet, row - 1, 7);
    }

    private static void WriteHeader(IXLWorksheet sheet, (string Text, double Width)[] columns)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            sheet.Cell(1, i + 1).SetValue(columns[i].Text);
            sheet.Column(i + 1).Width = columns[i].Width;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteTerritory(IXLRow row, RegisterTerritory territory)
    {
        row.Cell(1).SetValue(territory.MunicipalityName);
        row.Cell(2).SetValue(territory.Number);
        row.Cell(3).SetValue(territory.Name);
    }

    private static void SetDate(IXLCell cell, DateOnly? day)
    {
        if (day is { } value)
        {
            cell.SetValue(value.ToDateTime(TimeOnly.MinValue));
            cell.Style.NumberFormat.Format = DayNumberFormat;
        }
    }

    private static void Finish(IXLWorksheet sheet, int lastRow, int lastColumn) =>
        sheet.Range(1, 1, Math.Max(lastRow, 1), lastColumn).SetAutoFilter();
}
