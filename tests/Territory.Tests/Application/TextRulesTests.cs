using Territory.Application.Common;

namespace Territory.Tests.Application;

public sealed class TextRulesTests
{
    [Fact]
    public void RequiredText_TrimsValidText()
    {
        var errors = new ValidationErrors();

        Assert.Equal("Giano", TextRules.RequiredText(errors, "name", "  Giano ", 10));
        Assert.False(errors.HasErrors);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("   ", "required")]
    [InlineData("12345678901", "maxLength")]
    [InlineData("a\u0000b", "invalidFormat")]
    [InlineData("line\nbreak", "invalidFormat")]
    public void RequiredText_ReportsFieldCode(string? value, string code)
    {
        var errors = new ValidationErrors();

        TextRules.RequiredText(errors, "name", value, 10);

        Assert.Equal([code], errors.ToError().Fields!["name"]);
    }
}
