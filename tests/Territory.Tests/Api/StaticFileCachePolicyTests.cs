using Territory.Api;

namespace Territory.Tests.Api;

public sealed class StaticFileCachePolicyTests
{
    [Theory]
    [InlineData("main-SNBGSEDY.js")]
    [InlineData("chunk-du__UIA-.js")]
    [InlineData("chunk-BvJSK-Rk.js")]
    [InlineData("styles-2VEAF4TU.css")]
    [InlineData("roboto-latin-400-normal-LOX3CHMS.woff2")]
    [InlineData("roboto-latin-400-normal-54N5OGOO.woff")]
    public void HashedAssets_AreImmutable(string fileName)
    {
        Assert.Equal(StaticFileCachePolicy.Immutable, StaticFileCachePolicy.For(fileName));
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("it.json")]
    [InlineData("en.json")]
    [InlineData("favicon.ico")]
    [InlineData("3rdpartylicenses.txt")]
    [InlineData("main.js")]
    [InlineData("styles-short.css")]
    public void UnhashedFiles_AreRevalidated(string fileName)
    {
        Assert.Equal(StaticFileCachePolicy.Revalidate, StaticFileCachePolicy.For(fileName));
    }
}
