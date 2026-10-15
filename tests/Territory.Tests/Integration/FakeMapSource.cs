using Territory.Application.Maps;

namespace Territory.Tests.Integration;

/// <summary>Programmable <see cref="IMapSource"/> that records the requested map ids.</summary>
public sealed class FakeMapSource : IMapSource
{
    public string? Kml { get; set; }
    public List<string> Requests { get; } = [];

    public Task<string?> FetchKmlAsync(string mapId, CancellationToken cancellationToken)
    {
        Requests.Add(mapId);
        return Task.FromResult(Kml);
    }
}
