namespace Territory.Application.Maps;

/// <summary>
/// Minimal GeoJSON sent to the public page. Only geometry, a plain-text name and colours computed by the server:
/// KML descriptions, links and icon URLs never reach the browser.
/// </summary>
public sealed record GeoFeatureCollection(IReadOnlyList<GeoFeature> Features)
{
    public string Type => "FeatureCollection";
}

public sealed record GeoFeature(GeoGeometry Geometry, FeatureStyle Properties)
{
    public string Type => "Feature";
}

/// <param name="Coordinates">[lon, lat] for points, a list of them for lines, a list of rings for polygons.</param>
public sealed record GeoGeometry(string Type, object Coordinates);

/// <summary>CSS colours (rgba) and line width.</summary>
public sealed record FeatureStyle(string? Name, string Stroke, double StrokeWidth, string Fill);
