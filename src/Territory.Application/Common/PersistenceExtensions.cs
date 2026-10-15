using Microsoft.EntityFrameworkCore;
using Territory.Application.Abstractions;

namespace Territory.Application.Common;

public enum SaveOutcome
{
    Saved,
    UniqueViolation,
    ForeignKeyViolation
}

public static class PersistenceExtensions
{
    /// <summary>
    /// Saves and translates constraint violations into outcomes: database constraints are the authoritative
    /// guard against concurrent requests that pass the pre-checks.
    /// </summary>
    public static async Task<SaveOutcome> TrySaveAsync(this ITerritoryDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            return SaveOutcome.UniqueViolation;
        }
        catch (DbUpdateException ex) when (db.IsForeignKeyViolation(ex))
        {
            return SaveOutcome.ForeignKeyViolation;
        }
    }
}
