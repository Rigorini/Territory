using System.Globalization;
using Territory.Application.Domain;
using Territory.Application.Export;

namespace Territory.Tests.Application;

public sealed class RegisterTextsTests
{
    private static readonly DateOnly From = new(2025, 1, 1);
    private static readonly DateOnly To = new(2025, 12, 31);

    [Theory]
    [InlineData("en", "Territory register")]
    [InlineData("it", "Registro dei territori")]
    [InlineData(null, "Registro dei territori")]
    [InlineData("fr", "Registro dei territori")]
    public void For_FallsBackToItalian(string? language, string title) =>
        Assert.Equal(title, RegisterTexts.For(language).Title);

    [Fact]
    public void Period_DescribesEveryCombination()
    {
        var texts = RegisterTexts.Italian;

        Assert.Equal("Tutto lo storico", texts.Period(null, null));
        Assert.Equal("Periodo: dal 01/01/2025 al 31/12/2025", texts.Period(From, To));
        Assert.Equal("Periodo: dal 01/01/2025", texts.Period(From, null));
        Assert.Equal("Periodo: fino al 31/12/2025", texts.Period(null, To));
    }

    [Fact]
    public void Labels_DependOnValues()
    {
        var texts = RegisterTexts.English;
        var assigned = new RegisterTerritory("Giano", 1, "Centro", "Rossi Mario", From, []);

        Assert.Equal("Assigned", texts.StatusOf(assigned));
        Assert.Equal("Available", texts.StatusOf(assigned with { CurrentPublisher = null }));
        Assert.Equal("Publisher", texts.ReturnedByText(ReturnedBy.Publisher));
        Assert.Equal("Administrator", texts.ReturnedByText(ReturnedBy.Admin));
        Assert.Equal("Administrator", texts.ReturnedByText(ReturnedBy.Admin, ""));
        Assert.Equal("Anna Verdi", texts.ReturnedByText(ReturnedBy.Admin, "Anna Verdi"));
        Assert.Equal("Publisher", texts.ReturnedByText(ReturnedBy.Publisher, "Anna Verdi"));
        Assert.Equal(string.Empty, texts.ReturnedByText(null));
        Assert.Equal(string.Empty, texts.Date(null));
        Assert.Equal("Giano", texts.Scope("Giano"));
        Assert.Equal("All municipalities", texts.Scope(null));
    }

    [Fact]
    public void Date_IsDayMonthYearInEveryLanguageAndThreadCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var day = new DateOnly(2025, 3, 10);

            Assert.Equal("10/03/2025", RegisterTexts.Italian.Date(day));
            Assert.Equal("10/03/2025", RegisterTexts.English.Date(day));
            Assert.Equal("Generated on 10/03/2025", RegisterTexts.English.Generated(day));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
