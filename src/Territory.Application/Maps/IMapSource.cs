namespace Territory.Application.Maps;

/// <summary>Downloads the KML of a MyMaps map. The map id must already be validated.</summary>
public interface IMapSource
{
    /// <returns>The KML text, or null when the map cannot be downloaded (network error, not public, too large).</returns>
    Task<string?> FetchKmlAsync(string mapId, CancellationToken cancellationToken);
}
