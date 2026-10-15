namespace Territory.Tests.Application;

/// <summary>KML fragments shaped like the MyMaps export.</summary>
public static class KmlSamples
{
    public const string Polygon = """
        <Placemark>
          <name>Zona A</name>
          <styleUrl>#poly-map</styleUrl>
          <Polygon><outerBoundaryIs><LinearRing><coordinates>
            12.5,42.8,0 12.6,42.8,0 12.6,42.9,0 12.5,42.8,0
          </coordinates></LinearRing></outerBoundaryIs></Polygon>
        </Placemark>
        """;

    public static string Document(string placemarks, string styles = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2">
          <Document>
            <name>Territorio</name>
            <description><![CDATA[<a href="https://evil.example">x</a>]]></description>
            {styles}
            <Folder>{placemarks}</Folder>
          </Document>
        </kml>
        """;

    public const string PolygonStyles = """
        <Style id="poly-normal">
          <IconStyle><color>ff0000ff</color></IconStyle>
          <LineStyle><color>ff2f8f00</color><width>4.5</width></LineStyle>
          <PolyStyle><color>4d2f8f00</color><fill>1</fill></PolyStyle>
        </Style>
        <Style id="poly-highlight"><LineStyle><color>ffffffff</color></LineStyle></Style>
        <StyleMap id="poly-map">
          <Pair><key>highlight</key><styleUrl>#poly-highlight</styleUrl></Pair>
          <Pair><key>normal</key><styleUrl>#poly-normal</styleUrl></Pair>
        </StyleMap>
        """;

    public static string Valid => Document(Polygon, PolygonStyles);
}
