using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Territory.Infrastructure.Maps;

namespace Territory.Tests.Application;

public sealed class GoogleMyMapsKmlSourceTests
{
    private const string MapId = "map-id-0000000001";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fetch_RequestsKmlExportOfTheMap_AndStripsBom()
    {
        HttpRequestMessage? sent = null;
        var source = Create(request =>
        {
            sent = request;
            return Ok(new StringContent("\uFEFF<kml/>", Encoding.UTF8));
        });

        var kml = await source.FetchKmlAsync(MapId, Ct);

        Assert.Equal("<kml/>", kml);
        Assert.Equal(new Uri($"https://www.google.com/maps/d/kml?mid={MapId}&forcekml=1"), sent!.RequestUri);
        Assert.Equal(HttpMethod.Get, sent.Method);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("../../evil?x=1&y")]
    public async Task Fetch_WithInvalidMapId_DoesNotSendRequests(string mapId)
    {
        var calls = 0;
        var source = Create(_ =>
        {
            calls++;
            return Ok(new StringContent("<kml/>"));
        });

        Assert.Null(await source.FetchKmlAsync(mapId, Ct));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Fetch_WithErrorStatus_ReturnsNull()
    {
        var source = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await source.FetchKmlAsync(MapId, Ct));
    }

    [Fact]
    public async Task Fetch_WithDeclaredOversizeBody_ReturnsNull()
    {
        var content = new ByteArrayContent([1]);
        content.Headers.ContentLength = GoogleMyMapsKmlSource.MaxBytes + 1L;
        var source = Create(_ => Ok(content));

        Assert.Null(await source.FetchKmlAsync(MapId, Ct));
    }

    [Fact]
    public async Task Fetch_WithUndeclaredOversizeBody_StopsReading()
    {
        var source = Create(_ => Ok(new UnknownLengthContent(new byte[GoogleMyMapsKmlSource.MaxBytes + 1])));

        Assert.Null(await source.FetchKmlAsync(MapId, Ct));
    }

    [Fact]
    public async Task Fetch_WithNetworkError_ReturnsNull()
    {
        var source = Create(_ => throw new HttpRequestException("down"));

        Assert.Null(await source.FetchKmlAsync(MapId, Ct));
    }

    [Fact]
    public async Task Fetch_WithTimeout_ReturnsNull()
    {
        var source = Create(_ => throw new TaskCanceledException("timeout"));

        Assert.Null(await source.FetchKmlAsync(MapId, Ct));
    }

    [Fact]
    public async Task Fetch_WhenCallerCancels_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var source = Create(_ => Ok(new StringContent("<kml/>")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchKmlAsync(MapId, cts.Token));
    }

    private static GoogleMyMapsKmlSource Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = GoogleMyMapsKmlSource.BaseAddress };
        return new GoogleMyMapsKmlSource(http, NullLogger<GoogleMyMapsKmlSource>.Instance);
    }

    private static HttpResponseMessage Ok(HttpContent content) => new(HttpStatusCode.OK) { Content = content };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Body without Content-Length, like a chunked response.</summary>
    private sealed class UnknownLengthContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
