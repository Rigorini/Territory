using Territory.Application.Common;
using Territory.Application.Territories;

namespace Territory.Tests.Application;

public sealed class MyMapsLinkTests
{
    private const string MapId = "1AbCdEfGhIjKlMnOpQrStUvWxYz012345";

    [Theory]
    [InlineData("https://www.google.com/maps/d/viewer?mid=" + MapId + "&usp=sharing")]
    [InlineData("https://www.google.com/maps/d/u/0/edit?ll=43.1,12.5&z=12&mid=" + MapId)]
    [InlineData("https://google.it/maps/d/embed?mid=" + MapId)]
    [InlineData("  https://www.google.com/maps/d/viewer?mid=" + MapId + "  ")]
    public void TryExtractMapId_AcceptsMyMapsLinks(string url)
    {
        Assert.Equal(MapId, MyMapsLink.TryExtractMapId(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    [InlineData("http://www.google.com/maps/d/viewer?mid=" + MapId)]
    [InlineData("https://www.google.com:8443/maps/d/viewer?mid=" + MapId)]
    [InlineData("https://user@www.google.com/maps/d/viewer?mid=" + MapId)]
    [InlineData("https://evil.example/maps/d/viewer?mid=" + MapId)]
    [InlineData("https://www.google.com.evil.example/maps/d/viewer?mid=" + MapId)]
    [InlineData("https://www.google.com/maps/place/viewer?mid=" + MapId)]
    [InlineData("https://www.google.com/maps/d/viewer?id=" + MapId)]
    [InlineData("https://www.google.com/maps/d/viewer?mid=short")]
    [InlineData("https://www.google.com/maps/d/viewer?mid=abc%2F..%2Fdef1234567")]
    [InlineData("https://maps.app.goo.gl/abcdefghijk")]
    public void TryExtractMapId_RejectsOtherUrls(string? url)
    {
        Assert.Null(MyMapsLink.TryExtractMapId(url));
    }

    [Fact]
    public void TryExtractMapId_RejectsOverlongUrls()
    {
        var url = "https://www.google.com/maps/d/viewer?mid=" + MapId + "&x=" + new string('a', MyMapsLink.UrlMaxLength);

        Assert.Null(MyMapsLink.TryExtractMapId(url));
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData(" ", "required")]
    [InlineData("https://evil.example/", "invalidFormat")]
    public void Validate_ReportsFieldCode(string? url, string code)
    {
        var errors = new ValidationErrors();

        Assert.Null(MyMapsLink.Validate(errors, "mapUrl", url));
        Assert.Equal([code], errors.ToError().Fields!["mapUrl"]);
    }

    [Fact]
    public void ToViewerUrl_RoundTrips()
    {
        Assert.Equal(MapId, MyMapsLink.TryExtractMapId(MyMapsLink.ToViewerUrl(MapId)));
    }
}
