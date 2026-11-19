using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>Brings the database up to date when the application starts.</summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Applies pending EF Core migrations (creating the database when it does not exist). A database
    /// created by the original application is upgraded in place first, keeping its data. Providers
    /// without migrations get the schema from the model. Demonstration data is added only when
    /// <see cref="DataAccessOptions.SeedDemoData"/> is set, and only to an empty database.
    /// </summary>
    Task<DemoSeedOutcome> InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>What happened to the demonstration data at start-up.</summary>
public enum DemoSeedOutcome
{
    /// <summary>Demo mode is off.</summary>
    NotRequested = 0,

    /// <summary>The empty database received the demonstration data.</summary>
    Seeded = 1,

    /// <summary>Demo mode is on but the database has data; nothing was added.</summary>
    SkippedExistingData = 2,
}

/// <summary>Settings of the data access layer.</summary>
public sealed class DataAccessOptions
{
    /// <summary>
    /// Adds fictional demonstration accounts and films to an empty database. Off by default: a default
    /// installation must not contain any account with a known password.
    /// </summary>
    public bool SeedDemoData { get; init; }
}

internal sealed class DatabaseInitializer(
    IDbContextFactory<FilmDbContext> contextFactory,
    DataAccessOptions options) : IDatabaseInitializer
{
    public async Task<DemoSeedOutcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (db.Database.IsSqlServer())
            {
                // Schema changes of a large database may run long; ordinary operations keep the
                // configured timeout, as they use contexts of their own.
                db.Database.SetCommandTimeout(LegacySchemaUpgrader.CommandTimeout);

                // A database from the original application has the tables but no migration history.
                await LegacySchemaUpgrader.UpgradeIfLegacyAsync(db, cancellationToken).ConfigureAwait(false);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!options.SeedDemoData)
            {
                return DemoSeedOutcome.NotRequested;
            }

            return await DemoData.SeedIfEmptyAsync(db, cancellationToken).ConfigureAwait(false)
                ? DemoSeedOutcome.Seeded
                : DemoSeedOutcome.SkippedExistingData;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DatabaseInitializationException(ex);
        }
    }
}

/// <summary>Raised when the database cannot be created, upgraded or migrated at start-up.</summary>
public sealed class DatabaseInitializationException : Exception
{
    public DatabaseInitializationException()
        : base("Database initialization failed.")
    {
    }

    public DatabaseInitializationException(Exception innerException)
        : base("Database initialization failed.", innerException)
    {
    }

    public DatabaseInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
