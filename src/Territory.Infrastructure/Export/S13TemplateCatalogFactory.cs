using Microsoft.Extensions.Configuration;

namespace Territory.Infrastructure.Export;

/// <summary>Reads the "S13" configuration section: template folder and one template file name per language.</summary>
public static class S13TemplateCatalogFactory
{
    public const string SectionName = "S13";
    public const string DefaultFolder = "s13";

    public static S13TemplateCatalog FromConfiguration(IConfiguration configuration, string contentRoot)
    {
        var section = configuration.GetSection(SectionName);
        var templates = section.GetSection("Templates").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!, StringComparer.OrdinalIgnoreCase);
        return new S13TemplateCatalog(ResolveFolder(section["Folder"] ?? DefaultFolder, contentRoot), templates);
    }

    /// <summary>Relative folders are looked up in the content root, then next to the binaries (published layout).</summary>
    private static string ResolveFolder(string folder, string contentRoot)
    {
        if (Path.IsPathRooted(folder))
        {
            return folder;
        }

        var inContentRoot = Path.Combine(contentRoot, folder);
        return Directory.Exists(inContentRoot) ? inContentRoot : Path.Combine(AppContext.BaseDirectory, folder);
    }
}
