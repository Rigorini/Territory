using System.Text.Json;
using System.Xml;
using Territory.Application.Maps;

namespace Territory.Tests.Application;

public sealed class KmlConverterTests
{
    private const string DefaultStroke = "rgba(25,118,210,1)";
    private const string DefaultFill = "rgba(25,118,210,0.25)";

    [Fact]
    public void Polygon_UsesNormalStyleOfStyleMap_AndConvertsAabbggrrColours()
    {
        var map = KmlConverter.ToGeoJson(KmlSamples.Valid);

        var feature = Assert.Single(map.Features);
        Assert.Equal("FeatureCollection", map.Type);
        Assert.Equal("Feature", feature.Type);
        Assert.Equal("Polygon", feature.Geometry.Type);
        var rings = Assert.IsType<double[][][]>(feature.Geometry.Coordinates);
        Assert.Equal([12.5, 42.8], Assert.Single(rings)[0]);
        Assert.Equal(new FeatureStyle("Zona A", "rgba(0,143,47,1)", 4.5, "rgba(0,143,47,0.302)"), feature.Properties);
    }

    [Fact]
    public void Output_NeverContainsDescriptionsOrLinks()
    {
        var json = JsonSerializer.Serialize(KmlConverter.ToGeoJson(KmlSamples.Valid));

        Assert.DoesNotContain("evil", json, StringComparison.Ordinal);
        Assert.DoesNotContain("href", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiGeometry_IsFlattened_WithInlineStyle()
    {
        const string placemark = """
            <Placemark>
              <Style><LineStyle><color>80ff0000</color><width>99</width></LineStyle><PolyStyle><fill>0</fill></PolyStyle></Style>
              <MultiGeometry>
                <Point><coordinates>12.5,42.8</coordinates></Point>
                <LineString><coordinates>12.5,42.8 12.6,42.9</coordinates></LineString>
              </MultiGeometry>
            </Placemark>
            """;

        var features = KmlConverter.ToGeoJson(KmlSamples.Document(placemark)).Features;

        Assert.Equal(["Point", "LineString"], features.Select(f => f.Geometry.Type));
        Assert.Equal([12.5, 42.8], Assert.IsType<double[]>(features[0].Geometry.Coordinates));
        Assert.Equal(2, Assert.IsType<double[][]>(features[1].Geometry.Coordinates).Length);
        Assert.All(features, f => Assert.Equal(new FeatureStyle(null, "rgba(0,0,255,0.502)", 3, "rgba(0,0,0,0)"), f.Properties));
    }

    [Fact]
    public void PointStyle_FallsBackToIconColour()
    {
        const string styles = """<Style id="pin"><IconStyle><color>ff00ff00</color></IconStyle></Style>""";
        const string placemark = "<Placemark><styleUrl>#pin</styleUrl><Point><coordinates>1,2</coordinates></Point></Placemark>";

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark, styles)).Features);

        Assert.Equal(new FeatureStyle(null, "rgba(0,255,0,1)", 3, DefaultFill), feature.Properties);
    }

    [Theory]
    [InlineData("<styleUrl>https://example.com/styles.kml#s</styleUrl>")]
    [InlineData("<styleUrl>#missing</styleUrl>")]
    [InlineData("<styleUrl>#</styleUrl>")]
    [InlineData("")]
    public void UnknownOrRemoteStyle_UsesDefault(string styleUrl)
    {
        var placemark = $"<Placemark>{styleUrl}<Point><coordinates>1,2</coordinates></Point></Placemark>";

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark)).Features);

        Assert.Equal(new FeatureStyle(null, DefaultStroke, 3, DefaultFill), feature.Properties);
    }

    [Fact]
    public void InvalidColoursAndWidths_UseDefaults()
    {
        const string placemark = """
            <Placemark>
              <Style><LineStyle><color>red</color><width>abc</width></LineStyle><PolyStyle><color>zzzzzzzz</color></PolyStyle></Style>
              <Point><coordinates>1,2</coordinates></Point>
            </Placemark>
            """;

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark)).Features);

        Assert.Equal(new FeatureStyle(null, DefaultStroke, 3, DefaultFill), feature.Properties);
    }

    [Fact]
    public void StyleMapWithoutNormalPair_IsIgnored()
    {
        const string styles = """<StyleMap id="m"><Pair><key>highlight</key><styleUrl>#x</styleUrl></Pair></StyleMap>""";
        const string placemark = "<Placemark><styleUrl>#m</styleUrl><Point><coordinates>1,2</coordinates></Point></Placemark>";

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark, styles)).Features);

        Assert.Equal(DefaultStroke, feature.Properties.Stroke);
    }

    [Fact]
    public void InvalidCoordinates_AreSkipped_AndDegenerateGeometriesDropped()
    {
        const string placemarks = """
            <Placemark><Point><coordinates>200,10</coordinates></Point></Placemark>
            <Placemark><Point><coordinates>abc</coordinates></Point></Placemark>
            <Placemark><Point></Point></Placemark>
            <Placemark><LineString><coordinates>1,2 1,95 NaN,1 1</coordinates></LineString></Placemark>
            <Placemark><Polygon><outerBoundaryIs><LinearRing><coordinates>1,1 2,2 1,1</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
            <Placemark><Polygon></Polygon></Placemark>
            <Placemark><name>   </name></Placemark>
            <Placemark><Point><coordinates>12.123456789,-42.5,100</coordinates></Point></Placemark>
            """;

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemarks)).Features);

        Assert.Equal([12.1234568, -42.5], Assert.IsType<double[]>(feature.Geometry.Coordinates));
    }

    [Fact]
    public void Polygon_KeepsValidHoles()
    {
        const string placemark = """
            <Placemark><Polygon>
              <outerBoundaryIs><LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing></outerBoundaryIs>
              <innerBoundaryIs><LinearRing><coordinates>1,1 2,1 2,2 1,1</coordinates></LinearRing></innerBoundaryIs>
              <innerBoundaryIs><LinearRing><coordinates>1,1</coordinates></LinearRing></innerBoundaryIs>
            </Polygon></Placemark>
            """;

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark)).Features);

        Assert.Equal(2, Assert.IsType<double[][][]>(feature.Geometry.Coordinates).Length);
    }

    [Fact]
    public void LongNames_AreTruncated()
    {
        var placemark = $"<Placemark><name>{new string('x', 300)}</name><Point><coordinates>1,2</coordinates></Point></Placemark>";

        var feature = Assert.Single(KmlConverter.ToGeoJson(KmlSamples.Document(placemark)).Features);

        Assert.Equal(KmlConverter.MaxNameLength, feature.Properties.Name!.Length);
    }

    [Fact]
    public void Features_AreCapped()
    {
        var placemarks = string.Concat(Enumerable.Repeat("<Placemark><Point><coordinates>1,2</coordinates></Point></Placemark>", KmlConverter.MaxFeatures + 5));

        Assert.Equal(KmlConverter.MaxFeatures, KmlConverter.ToGeoJson(KmlSamples.Document(placemarks)).Features.Count);
    }

    [Theory]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE kml [<!ENTITY x SYSTEM "file:///etc/passwd">]><kml>&x;</kml>""")]
    [InlineData("<html><body>Sign in</body></html>")]
    [InlineData("<!doctype html><html>")]
    [InlineData("not xml")]
    public void NonKmlOrDtd_IsRejected(string text)
    {
        Assert.ThrowsAny<XmlException>(() => KmlConverter.ToGeoJson(text));
    }
}
