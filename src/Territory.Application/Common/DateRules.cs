namespace Territory.Application.Common;

/// <summary>Validation of event dates entered by admins (assignment and return).</summary>
public static class DateRules
{
    public const string InFuture = "inFuture";

    /// <summary>Earliest accepted date: older records belong to the paper register.</summary>
    public static readonly DateTimeOffset MinDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Tolerates small clock differences between the browser and the server.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <returns>The given date, or now when missing.</returns>
    public static DateTimeOffset PastOrNow(ValidationErrors errors, string field, DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return now;
        }

        errors.AddIf(value > now + ClockSkew, field, InFuture);
        errors.AddIf(value < MinDate, field, TextRules.OutOfRange);
        return value.Value > now ? now : value.Value;
    }
}
