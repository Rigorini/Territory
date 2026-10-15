using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Territory.Application.Abstractions;
using Territory.Application.Assignments;
using Territory.Application.Common;
using Territory.Application.Domain;
using Territory.Application.Maps;
using Territory.Application.Territories;

namespace Territory.Application.Public;

public sealed record PublicTerritoryDto(
    string MunicipalityName, int Number, string Name, string PublisherName, DateTimeOffset AssignedAt);

public sealed record ReturnReceiptDto(DateTimeOffset AssignedAt, DateTimeOffset ReturnedAt);

/// <summary>
/// Anonymous access through a share token. Malformed, unknown and closed tokens all produce the same error,
/// so the endpoint does not reveal whether a link ever existed.
/// </summary>
public sealed partial class PublicTerritoryService(
    ITerritoryDbContext db, IMapSource mapSource, TimeProvider clock, ILogger<PublicTerritoryService> logger)
{
    public const string InvalidLink = "public.invalidLink";
    public const string MapUnavailable = "public.mapUnavailable";
    public static readonly TimeSpan MapCacheDuration = TimeSpan.FromHours(1);

    private sealed record CachedMap(int TerritoryId, string MapId, string? Kml, DateTimeOffset? FetchedAt);

    public async Task<Result<PublicTerritoryDto>> GetAsync(string? token, CancellationToken cancellationToken)
    {
        if (TryHash(token) is not { } hash)
        {
            return AppError.NotFound(InvalidLink);
        }

        var row = await OpenAssignment(hash)
            .Select(a => new
            {
                Municipality = a.Territory!.Municipality!.Name,
                a.Territory.Number,
                a.Territory.Name,
                a.Publisher!.FirstName,
                a.Publisher.LastName,
                a.AssignedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? AppError.NotFound(InvalidLink)
            : new PublicTerritoryDto(row.Municipality, row.Number, row.Name, $"{row.LastName} {row.FirstName}", row.AssignedAt);
    }

    /// <summary>Serves the territory map from the cache, refreshing it from MyMaps when older than <see cref="MapCacheDuration"/>.</summary>
    public async Task<Result<GeoFeatureCollection>> GetMapAsync(string? token, CancellationToken cancellationToken)
    {
        if (TryHash(token) is not { } hash)
        {
            return AppError.NotFound(InvalidLink);
        }

        var cached = await OpenAssignment(hash)
            .Select(a => new CachedMap(a.TerritoryId, a.Territory!.MyMapsId, a.Territory.MapKml, a.Territory.MapFetchedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (cached is null)
        {
            return AppError.NotFound(InvalidLink);
        }

        var map = IsFresh(cached) ? TryConvert(cached.Kml) : await RefreshAsync(cached, cancellationToken);
        return map ?? TryConvert(cached.Kml) ?? (Result<GeoFeatureCollection>)AppError.Unavailable(MapUnavailable);
    }

    /// <summary>Closes the assignment on behalf of the publisher; the link stops working immediately.</summary>
    public async Task<Result<ReturnReceiptDto>> ReturnAsync(string? token, CancellationToken cancellationToken)
    {
        if (TryHash(token) is not { } hash)
        {
            return AppError.NotFound(InvalidLink);
        }

        var assignment = await OpenAssignment(hash)
            .Select(a => new { a.Id, a.AssignedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (assignment is null)
        {
            return AppError.NotFound(InvalidLink);
        }

        var returnedAt = Max(clock.GetUtcNow(), assignment.AssignedAt);

        // The token hash in the filter makes a concurrent return or link renewal win over this one.
        var updated = await db.Assignments
            .Where(a => a.Id == assignment.Id && a.TokenHash == hash && a.ReturnedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ReturnedAt, returnedAt)
                .SetProperty(a => a.ReturnedBy, ReturnedBy.Publisher)
                .SetProperty(a => a.TokenHash, (byte[]?)null), cancellationToken);
        if (updated == 0)
        {
            return AppError.NotFound(InvalidLink);
        }

        LogReturned(assignment.Id);
        return new ReturnReceiptDto(assignment.AssignedAt, returnedAt);
    }

    private static byte[]? TryHash(string? token) => ShareToken.IsWellFormed(token) ? ShareToken.Hash(token!) : null;

    private IQueryable<Assignment> OpenAssignment(byte[] hash) =>
        db.Assignments.AsNoTracking().Where(a => a.TokenHash == hash && a.ReturnedAt == null);

    private bool IsFresh(CachedMap cached) =>
        cached.Kml is not null && cached.FetchedAt + MapCacheDuration > clock.GetUtcNow();

    private async Task<GeoFeatureCollection?> RefreshAsync(CachedMap cached, CancellationToken cancellationToken)
    {
        var kml = MyMapsLink.IsValidMapId(cached.MapId) ? await mapSource.FetchKmlAsync(cached.MapId, cancellationToken) : null;
        var map = TryConvert(kml);
        if (map is null)
        {
            LogMapUnavailable(cached.TerritoryId);
            return null;
        }

        // Matching the map id avoids overwriting the cache of a territory whose map changed meanwhile.
        var now = clock.GetUtcNow();
        await db.Territories
            .Where(t => t.Id == cached.TerritoryId && t.MyMapsId == cached.MapId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.MapKml, kml)
                .SetProperty(t => t.MapFetchedAt, now), cancellationToken);
        return map;
    }

    private static GeoFeatureCollection? TryConvert(string? kml)
    {
        if (kml is null)
        {
            return null;
        }

        try
        {
            return KmlConverter.ToGeoJson(kml);
        }
        catch (XmlException)
        {
            // Not KML (e.g. the Google sign-in page of a private map): treated as an unavailable map.
            return null;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    [LoggerMessage(Level = LogLevel.Information, Message = "Assignment {AssignmentId} returned by the publisher")]
    private partial void LogReturned(long assignmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map of territory {TerritoryId} could not be downloaded or is not valid KML")]
    private partial void LogMapUnavailable(int territoryId);
}
