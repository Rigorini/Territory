using System.Text;
using Microsoft.Extensions.Logging;
using Territory.Application.Maps;
using Territory.Application.Territories;

namespace Territory.Infrastructure.Maps;

/// <summary>
/// Downloads the KML export of a Google MyMaps map. The URL is built here from a validated map id only (no SSRF),
/// and the response is bounded in time and size. The map must be shared as "anyone with the link".
/// </summary>
public sealed partial class GoogleMyMapsKmlSource(HttpClient http, ILogger<GoogleMyMapsKmlSource> logger) : IMapSource
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static readonly Uri BaseAddress = new("https://www.google.com/");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public const int MaxRedirects = 3;

    public async Task<string?> FetchKmlAsync(string mapId, CancellationToken cancellationToken)
    {
        if (!MyMapsLink.IsValidMapId(mapId))
        {
            return null;
        }

        try
        {
            using var response = await http.GetAsync(
                $"maps/d/kml?mid={Uri.EscapeDataString(mapId)}&forcekml=1",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                LogRejected((int)response.StatusCode);
                return null;
            }

            return await ReadBoundedAsync(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            LogFailed(ex.GetType().Name);
            return null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(ex.GetType().Name);
            return null;
        }
    }

    /// <returns>The body as UTF-8 text, or null when it exceeds <see cref="MaxBytes"/>.</returns>
    private async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                LogRejected(0);
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF');
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MyMaps KML response rejected (status {StatusCode}; 0 means too large)")]
    private partial void LogRejected(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MyMaps KML download failed: {ErrorType}")]
    private partial void LogFailed(string errorType);
}
