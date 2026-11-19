using FilmKiralama.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// A private SQLite in-memory database wired through the application's own registrations. The
/// connection stays open for the lifetime of the instance, which keeps the database alive. The schema
/// comes from the EF Core model. With <c>demo</c> on (the default for these tests) the demonstration
/// data is added as with FILM_DEMO=1; without it the database is empty, as in a default installation.
/// </summary>
public sealed class SqliteDatabase(bool demo = true) : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider? _provider;

    public FakeTimeProvider Clock { get; } = new(TestServices.Start);

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized.");

    public DemoSeedOutcome SeedOutcome { get; private set; }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public FilmDbContext CreateContext() =>
        Services.GetRequiredService<IDbContextFactory<FilmDbContext>>().CreateDbContext();

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDataAccess(options => options.UseSqlite(_connection), new DataAccessOptions { SeedDemoData = demo });
        services.AddTestBusinessServices(Clock);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        SeedOutcome = await Get<IDatabaseInitializer>().InitializeAsync();
    }

    /// <summary>Another set of the application's services on the same database, with demo mode as given.</summary>
    public ServiceProvider BuildServices(bool demo = false)
    {
        var services = new ServiceCollection();
        services.AddDataAccess(options => options.UseSqlite(_connection), new DataAccessOptions { SeedDemoData = demo });
        services.AddTestBusinessServices(Clock);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }
}
