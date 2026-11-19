using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>
/// The database's clock: SYSUTCDATETIME() on SQL Server, the same clock the setup key expiry is checked
/// with. Providers without a server clock (the tests' SQLite) use <paramref name="fallback"/>.
/// </summary>
internal sealed class DatabaseClock(IDbContextFactory<FilmDbContext> contextFactory, TimeProvider fallback) : IClock
{
    public async Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (!db.Database.IsSqlServer())
        {
            return fallback.GetUtcNow().UtcDateTime;
        }

        var now = await db.Database
            .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
