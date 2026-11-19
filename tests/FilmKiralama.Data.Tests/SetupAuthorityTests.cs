using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// The database owner's setup key script and the application login's rights on a real SQL Server
/// instance (see <see cref="SqlServerTestDatabase"/>).
/// </summary>
public sealed class SetupAuthorityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnersKey_CreatesTheFirstAdminOfADatabaseWithAccountsOnce()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        await RegisterAsync(a, "musteri", 1);

        var key = await database.IssueSetupKeyAsync(Ct);
        Assert.Matches("^([0-9A-F]{4}-){7}[0-9A-F]{4}$", key);
        // Only the hash of the key is stored.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE AnahtarOzeti = HASHBYTES('SHA2_256', CAST(REPLACE(@key, '-', '') AS varchar(32)))",
            Ct,
            ("@key", key)));

        // Two founders with the same key at the same moment: one admin, the key used once.
        var results = await Task.WhenAll(
            Try(() => a.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct)),
            Try(() => b.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key.ToLowerInvariant(), "kurucu2", Password, Password, Ct)));

        Assert.Single(results, r => r is null);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi IS NOT NULL", Ct));
    }

    [Fact]
    public async Task OwnersKey_ExpiredByTheDatabaseClock_IsRefused()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await RegisterAsync(provider, "musteri", 1);
        var key = await database.IssueSetupKeyAsync(Ct);
        await database.ExecuteAsync("UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(SECOND, -1, SYSUTCDATETIME())", Ct);

        // The application's own clock still says the key is fresh; the database clock decides.
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => provider.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct));

        Assert.Equal(SetupService.SetupKeyInvalid, error.Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
    }

    [Fact]
    public async Task OwnersScript_WithAnAdmin_IssuesNoKey()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        var error = await Assert.ThrowsAsync<SqlException>(() => database.IssueSetupKeyAsync(Ct));

        Assert.Equal(50001, error.Number);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari", Ct));
    }

    [Fact]
    public async Task AppLoginRights_AllowTheApplicationButNoKeyChanges()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await database.ExecuteAsync("CREATE USER FilmKiralamaApp WITHOUT LOGIN;", Ct);

        await database.ExecuteAsync(await File.ReadAllTextAsync(SqlServerTestDatabase.RepositoryFile("docs", "grant-app-login.sql"), Ct), Ct);

        const string Rights = """
            EXECUTE AS USER = N'FilmKiralamaApp';
            SELECT HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'INSERT')
                 + HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.Filmler', N'OBJECT', N'DELETE')
                 + HAS_PERMS_BY_NAME(N'dbo.Kullanicilar', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'SELECT')
                 + HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'UPDATE', N'Tuketildi', N'COLUMN')
                 + HAS_PERMS_BY_NAME(N'dbo.Degerlendirmeler', N'OBJECT', N'INSERT')
                 + HAS_PERMS_BY_NAME(N'dbo.Degerlendirmeler', N'OBJECT', N'UPDATE')
                 + HAS_PERMS_BY_NAME(N'dbo.Degerlendirmeler', N'OBJECT', N'DELETE')
                 + HAS_PERMS_BY_NAME(N'dbo.IzlemeListesi', N'OBJECT', N'INSERT')
                 + HAS_PERMS_BY_NAME(N'dbo.IzlemeListesi', N'OBJECT', N'DELETE')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.IzlemeListesi', N'OBJECT', N'UPDATE')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.Kiralama', N'OBJECT', N'DELETE')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'INSERT')
                 + 10 * HAS_PERMS_BY_NAME(N'dbo.KurulumAnahtarlari', N'OBJECT', N'UPDATE', N'SonKullanma', N'COLUMN');
            REVERT;
            """;
        Assert.Equal(11, await database.ScalarAsync(Rights, Ct));
    }

    private static async Task<Exception?> Try(Func<Task> operation)
    {
        try
        {
            await Task.Run(operation, Ct);
            return null;
        }
        catch (BusinessRuleException ex)
        {
            return ex;
        }
    }
}
