using System.Globalization;
using System.Xml.Linq;

namespace Territory.Application.Maps;

internal sealed record KmlStyle(string Stroke, double StrokeWidth, string Fill)
{
    public const string DefaultStroke = "rgba(25,118,210,1)";
    public const string DefaultFill = "rgba(25,118,210,0.25)";
    public const string NoFill = "rgba(0,0,0,0)";
    public const double DefaultWidth = 3;

    public static readonly KmlStyle Default = new(DefaultStroke, DefaultWidth, DefaultFill);
}

/// <summary>Shared KML styles (Style and StyleMap by id) resolved for each placemark.</summary>
internal sealed class KmlStyles
{
    private const double MaxStrokeWidth = 20;
    private readonly Dictionary<string, KmlStyle> styles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> styleMaps = new(StringComparer.Ordinal);

    public static KmlStyles Read(XDocument document)
    {
        var result = new KmlStyles();
        foreach (var element in document.Descendants())
        {
            var id = (string?)element.Attribute("id");
            if (id is null)
            {
                continue;
            }

            result.Register(element, id);
        }

        return result;
    }

    /// <summary>Inline style first, then the referenced Style or the "normal" entry of a StyleMap.</summary>
    public KmlStyle? Resolve(XElement placemark)
    {
        var inline = KmlConverter.Child(placemark, "Style");
        if (inline is not null)
        {
            return Parse(inline);
        }

        var url = LocalId(KmlConverter.Child(placemark, "styleUrl")?.Value);
        if (url is not null && styleMaps.TryGetValue(url, out var normal))
        {
            url = normal;
        }

        return url is not null && styles.TryGetValue(url, out var style) ? style : null;
    }

    private void Register(XElement element, string id)
    {
        if (element.Name.LocalName == "Style")
        {
            styles[id] = Parse(element);
        }
        else if (element.Name.LocalName == "StyleMap" && NormalStyleUrl(element) is { } normal)
        {
            styleMaps[id] = normal;
        }
    }

    private static string? NormalStyleUrl(XElement styleMap) =>
        styleMap.Elements()
            .Where(p => p.Name.LocalName == "Pair" && KmlConverter.Child(p, "key")?.Value.Trim() == "normal")
            .Select(p => LocalId(KmlConverter.Child(p, "styleUrl")?.Value))
            .FirstOrDefault(u => u is not null);

    /// <summary>Only same-document references ("#id"): remote style URLs are ignored.</summary>
    private static string? LocalId(string? url)
    {
        var trimmed = url?.Trim();
        return trimmed is { Length: > 1 } && trimmed[0] == '#' ? trimmed[1..] : null;
    }

    private static KmlStyle Parse(XElement style)
    {
        var line = KmlConverter.Child(style, "LineStyle");
        var poly = KmlConverter.Child(style, "PolyStyle");
        var icon = KmlConverter.Child(style, "IconStyle");
        var stroke = Color(line) ?? Color(icon) ?? KmlStyle.DefaultStroke;
        var width = ReadWidth(line);
        var fill = HasFill(poly) ? Color(poly) ?? KmlStyle.DefaultFill : KmlStyle.NoFill;
        return new KmlStyle(stroke, width, fill);
    }

    private static string? Color(XElement? style) => ToCss(KmlConverter.Child(style, "color")?.Value);

    private static double ReadWidth(XElement? line)
    {
        var text = KmlConverter.Child(line, "width")?.Value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && width is > 0 and <= MaxStrokeWidth
            ? width
            : KmlStyle.DefaultWidth;
    }

    private static bool HasFill(XElement? poly) => KmlConverter.Child(poly, "fill")?.Value.Trim() != "0";

    /// <summary>KML colours are "aabbggrr" hex; the output is rebuilt from numbers, never copied as text.</summary>
    internal static string? ToCss(string? kmlColor)
    {
        var hex = kmlColor?.Trim();
        if (hex is not { Length: 8 } || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var alpha = (value >> 24) / 255d;
        var blue = (value >> 16) & 0xFF;
        var green = (value >> 8) & 0xFF;
        var red = value & 0xFF;
        return string.Create(CultureInfo.InvariantCulture, $"rgba({red},{green},{blue},{Math.Round(alpha, 3)})");
    }
}
