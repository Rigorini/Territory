namespace Territory.Api.Logging;

/// <summary>Daily rolling file that keeps warnings, errors and the security audit trail.</summary>
public sealed class FileLogOptions
{
    public const string SectionName = "FileLogging";
    public const int MaxRetainedFiles = 366;
    public const int MaxFileSizeMegabytes = 1024;

    public bool Enabled { get; set; } = true;

    /// <summary>Relative paths are resolved against the content root; the date is appended before the extension.</summary>
    public string Path { get; set; } = "logs/territory-.log";

    public int RetainedFileCountLimit { get; set; } = 60;

    /// <summary>Per-day cap: once reached, further events of that day are dropped to protect the disk.</summary>
    public int FileSizeLimitMegabytes { get; set; } = 10;

    public long FileSizeLimitBytes => FileSizeLimitMegabytes * 1024L * 1024L;

    public static FileLogOptions Read(IConfiguration configuration, string contentRootPath)
    {
        var options = configuration.GetSection(SectionName).Get<FileLogOptions>() ?? new FileLogOptions();
        options.Validate();
        options.Path = System.IO.Path.GetFullPath(options.Path, contentRootPath);
        return options;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            throw Invalid(nameof(Path), "must not be empty");
        }

        if (RetainedFileCountLimit is < 1 or > MaxRetainedFiles)
        {
            throw Invalid(nameof(RetainedFileCountLimit), $"must be between 1 and {MaxRetainedFiles}");
        }

        if (FileSizeLimitMegabytes is < 1 or > MaxFileSizeMegabytes)
        {
            throw Invalid(nameof(FileSizeLimitMegabytes), $"must be between 1 and {MaxFileSizeMegabytes}");
        }
    }

    private static InvalidOperationException Invalid(string key, string rule) =>
        new($"Invalid configuration value '{SectionName}:{key}': {rule}.");
}
