using System.Buffers.Text;
using Territory.Application.Assignments;
using Territory.Application.Common;

namespace Territory.Tests.Application;

public sealed class ShareTokenTests
{
    [Fact]
    public void Issue_ReturnsUrlSafe256BitToken_WithMatchingHash()
    {
        var issued = ShareToken.Issue();

        Assert.Equal(ShareToken.TokenLength, issued.Token.Length);
        Assert.Equal(32, Base64Url.DecodeFromChars(issued.Token).Length);
        Assert.True(ShareToken.IsWellFormed(issued.Token));
        Assert.Equal(ShareToken.Hash(issued.Token), issued.Hash);
        Assert.Equal(32, issued.Hash.Length);
    }

    [Fact]
    public void Issue_IsUnique()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => ShareToken.Issue().Token).ToHashSet();

        Assert.Equal(100, tokens.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void IsWellFormed_RejectsWrongShape(string? token) => Assert.False(ShareToken.IsWellFormed(token));

    [Fact]
    public void IsWellFormed_AcceptsUrlSafeAlphabet() =>
        Assert.True(ShareToken.IsWellFormed("Az09-_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
}

public sealed class DateRulesTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_DefaultsToNow()
    {
        var errors = new ValidationErrors();

        Assert.Equal(Now, DateRules.PastOrNow(errors, "d", null, Now));
        Assert.False(errors.HasErrors);
    }

    [Fact]
    public void WithinClockSkew_IsClampedToNow()
    {
        var errors = new ValidationErrors();

        Assert.Equal(Now, DateRules.PastOrNow(errors, "d", Now.AddMinutes(4), Now));
        Assert.False(errors.HasErrors);
    }

    [Fact]
    public void Past_IsKept()
    {
        var errors = new ValidationErrors();

        Assert.Equal(Now.AddDays(-3), DateRules.PastOrNow(errors, "d", Now.AddDays(-3), Now));
        Assert.False(errors.HasErrors);
    }

    [Fact]
    public void Future_IsRejected()
    {
        var errors = new ValidationErrors();

        DateRules.PastOrNow(errors, "d", Now.AddHours(1), Now);

        Assert.Equal([DateRules.InFuture], errors.ToError().Fields!["d"]);
    }

    [Fact]
    public void BeforeMinDate_IsOutOfRange()
    {
        var errors = new ValidationErrors();

        DateRules.PastOrNow(errors, "d", DateRules.MinDate.AddDays(-1), Now);

        Assert.Equal([TextRules.OutOfRange], errors.ToError().Fields!["d"]);
    }
}
