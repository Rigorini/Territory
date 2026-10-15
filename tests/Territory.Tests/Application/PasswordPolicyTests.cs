using Territory.Application.Security;

namespace Territory.Tests.Application;

public class PasswordPolicyTests
{
    [Fact]
    public void Validate_ComplexPassword_ReturnsNoViolations()
    {
        Assert.Empty(PasswordPolicy.Validate("Corretta!Password9"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_Empty_ReturnsRequiredOnly(string? password)
    {
        Assert.Equal([PasswordRule.Required], PasswordPolicy.Validate(password));
    }

    [Theory]
    [InlineData("Short!1a", PasswordRule.TooShort)]
    [InlineData("nouppercase!123", PasswordRule.MissingUppercase)]
    [InlineData("NOLOWERCASE!123", PasswordRule.MissingLowercase)]
    [InlineData("NoDigitsHere!!", PasswordRule.MissingDigit)]
    [InlineData("NoSymbolHere123", PasswordRule.MissingSymbol)]
    public void Validate_WeakPassword_ReportsSpecificRule(string password, PasswordRule expected)
    {
        Assert.Equal([expected], PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Validate_TooLong_ReportsTooLong()
    {
        var password = "Aa1!" + new string('x', PasswordPolicy.MaxLength);

        Assert.Equal([PasswordRule.TooLong], PasswordPolicy.Validate(password));
    }
}
