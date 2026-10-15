using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Territory.Application.Maps;

/// <summary>
/// Converts a MyMaps KML document into sanitised GeoJSON: placemark geometries, names and colours only.
/// The parser refuses DTDs and external entities, and output is bounded in size.
/// </summary>
public static class KmlConverter
{
    public const int MaxDocumentChars = 5 * 1024 * 1024;
    public const int MaxFeatures = 2000;
    public const int MaxNameLength = 200;

    /// <exception cref="XmlException">The text is not a DTD-free KML document.</exception>
    public static GeoFeatureCollection ToGeoJson(string kml)
    {
        var document = Load(kml);
        if (document.Root?.Name.LocalName != "kml")
        {
            throw new XmlException("The document is not KML.");
        }

        var styles = KmlStyles.Read(document);
        var features = document.Descendants()
            .Where(e => e.Name.LocalName == "Placemark")
            .SelectMany(p => ToFeatures(p, styles))
            .Take(MaxFeatures)
            .ToList();
        return new GeoFeatureCollection(features);
    }

    private static XDocument Load(string kml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentChars,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        using var reader = XmlReader.Create(new StringReader(kml), settings);
        return XDocument.Load(reader);
    }

    private static IEnumerable<GeoFeature> ToFeatures(XElement placemark, KmlStyles styles)
    {
        var style = styles.Resolve(placemark) ?? KmlStyle.Default;
        var properties = new FeatureStyle(ReadName(placemark), style.Stroke, style.StrokeWidth, style.Fill);
        return Geometries(placemark)
            .Select(ToGeometry)
            .OfType<GeoGeometry>()
            .Select(g => new GeoFeature(g, properties));
    }

    private static string? ReadName(XElement placemark)
    {
        var name = Child(placemark, "name")?.Value.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }

    /// <summary>Simple geometries of a placemark, flattening MultiGeometry.</summary>
    private static IEnumerable<XElement> Geometries(XElement placemark) =>
        placemark.Descendants().Where(e => e.Name.LocalName is "Point" or "LineString" or "Polygon");

    private static GeoGeometry? ToGeometry(XElement geometry) => geometry.Name.LocalName switch
    {
        "Point" => ToPoint(geometry),
        "LineString" => ToLine(geometry),
        _ => ToPolygon(geometry)
    };

    private static GeoGeometry? ToPoint(XElement point)
    {
        var positions = Coordinates(Child(point, "coordinates"));
        return positions.Length == 0 ? null : new GeoGeometry("Point", positions[0]);
    }

    private static GeoGeometry? ToLine(XElement line)
    {
        var positions = Coordinates(Child(line, "coordinates"));
        return positions.Length < 2 ? null : new GeoGeometry("LineString", positions);
    }

    private static GeoGeometry? ToPolygon(XElement polygon)
    {
        var outer = Ring(polygon.Elements().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs"));
        if (outer is null)
        {
            return null;
        }

        var inner = polygon.Elements()
            .Where(e => e.Name.LocalName == "innerBoundaryIs")
            .Select(Ring)
            .OfType<double[][]>();
        return new GeoGeometry("Polygon", inner.Prepend(outer).ToArray());
    }

    private static double[][]? Ring(XElement? boundary)
    {
        var ring = boundary?.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
        var positions = Coordinates(ring);
        return positions.Length < 4 ? null : positions;
    }

    /// <summary>"lon,lat[,alt]" tuples separated by whitespace; invalid tuples are skipped.</summary>
    private static double[][] Coordinates(XElement? element)
    {
        if (element is null)
        {
            return [];
        }

        return element.Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePosition)
            .OfType<double[]>()
            .ToArray();
    }

    private static double[]? ParsePosition(string tuple)
    {
        var parts = tuple.Split(',');
        if (parts.Length < 2
            || !TryParse(parts[0], out var lon) || !TryParse(parts[1], out var lat)
            || Math.Abs(lon) > 180 || Math.Abs(lat) > 90)
        {
            return null;
        }

        return [Math.Round(lon, 7), Math.Round(lat, 7)];
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    internal static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
}
