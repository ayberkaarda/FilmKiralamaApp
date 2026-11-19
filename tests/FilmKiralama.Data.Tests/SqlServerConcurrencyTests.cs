using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// Races on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>), each side with its own
/// services and connection pool, as two application instances: customers race for the last copy, one
/// customer races itself, a rental is returned twice at once, failed sign-ins arrive together and two
/// admins demote each other.
/// </summary>
public sealed class SqlServerConcurrencyTests
{
    private const int Rounds = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rent_TwoCustomersRaceForTheLastCopy_ExactlyOneGetsIt()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var first = await RegisterAsync(a, "birinci", 1);
        var second = await RegisterAsync(a, "ikinci", 2);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Son Kopya " + round, copies: 1);

            var results = await RaceAsync(
                [() => RentAsync(a, first, filmId), () => RentAsync(b, second, filmId)]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.NotAvailable, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            await AssertStockAsync(database, filmId, active: 1, available: 0);
        }
    }

    [Fact]
    public async Task Rent_ManyCustomersRaceForTwoCopies_StockNeverGoesBelowZero()
    {
        const int Customers = 12;
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var sessions = new List<UserSession>();
        for (var i = 0; i < Customers; i++)
        {
            sessions.Add(await RegisterAsync(a, "musteri" + i, 100 + i));
        }

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Iki Kopya " + round, copies: 2);

            var results = await RaceAsync(sessions.Select((s, i) => (Func<Task>)(() => RentAsync(i % 2 == 0 ? a : b, s, filmId))).ToList());

            Assert.Equal(2, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.Equal(RentalService.NotAvailable, Assert.IsType<BusinessRuleException>(r).Message));
            await AssertStockAsync(database, filmId, active: 2, available: 0);
        }
    }

    [Fact]
    public async Task Rent_OneCustomerRacesItself_HasOneActiveRental()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "tekrar", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Tekrar " + round, copies: 5);

            var results = await RaceAsync(Enumerable.Range(0, 8).Select(i => (Func<Task>)(() => RentAsync(i % 2 == 0 ? a : b, customer, filmId))).ToList());

            Assert.Single(results, r => r is null);
            Assert.All(results.Where(r => r is not null), r => Assert.Equal(RentalService.AlreadyRented, Assert.IsType<BusinessRuleException>(r).Message));
            await AssertStockAsync(database, filmId, active: 1, available: 4);
        }
    }

    [Fact]
    public async Task Return_SameRentalTwiceAtOnce_PutsOneCopyBack()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "iade", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Iade " + round, copies: 1);
            var receipt = await RentAsync(a, customer, filmId);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IRentalService>().ReturnAsync(customer, receipt.RentalId, Ct),
                () => b.GetRequiredService<IRentalService>().ReturnAsync(Admin(SeedAdminId), receipt.RentalId, Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.RentalNotFound, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            await AssertStockAsync(database, filmId, active: 0, available: 1);
        }
    }

    [Fact]
    public async Task SignIn_ConcurrentFailures_AreAllCountedAndLockTheAccount()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        await RegisterAsync(a, "kilit", 1);

        await RaceAsync(Enumerable.Range(0, 5).Select(i => (Func<Task>)(() => (i % 2 == 0 ? a : b).GetRequiredService<IAuthService>().SignInAsync("kilit", "Yanlis.Sifre1", Ct))).ToList());

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => a.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND KilitBitis IS NOT NULL AND BasarisizGiris = 0", Ct));
    }

    [Fact]
    public async Task UpdateAccount_TwoAdminsDemoteEachOther_OneAdminRemains()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();

        for (var round = 0; round < Rounds; round++)
        {
            var accounts = a.GetRequiredService<IAccountService>();
            var x = await accounts.AddAsync(Admin(SeedAdminId), Input("xadmin" + round, 200 + round, UserRole.Admin), Ct);
            var y = await accounts.AddAsync(Admin(SeedAdminId), Input("yadmin" + round, 300 + round, UserRole.Admin), Ct);

            // The seed admin steps down first, so x and y are the only admins.
            await accounts.UpdateAsync(Admin(x), SeedAdminId, new AccountInput("yonetici", null, "yonetici@example.com", "5550000001", UserRole.User), Ct);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IAccountService>().UpdateAsync(Admin(x), y, Input("yadmin" + round, 300 + round, UserRole.User), Ct),
                () => b.GetRequiredService<IAccountService>().UpdateAsync(Admin(y), x, Input("xadmin" + round, 200 + round, UserRole.User), Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(AccountService.LastAdmin, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));

            // Hand the role back to the seed admin for the next round.
            var remaining = await database.ScalarAsync("SELECT KullaniciID FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct);
            await accounts.UpdateAsync(Admin(remaining), SeedAdminId, new AccountInput("yonetici", null, "yonetici@example.com", "5550000001", UserRole.Admin), Ct);
            await accounts.UpdateAsync(Admin(SeedAdminId), remaining, remaining == x ? Input("xadmin" + round, 200 + round, UserRole.User) : Input("yadmin" + round, 300 + round, UserRole.User), Ct);
        }
    }

    private static AccountInput Input(string name, int number, UserRole role) =>
        new(name, Password, name + "@example.com", "553" + number.ToString("0000000", System.Globalization.CultureInfo.InvariantCulture), role);

    private static async Task AssertStockAsync(SqlServerTestDatabase database, int filmId, int active, int available)
    {
        Assert.Equal(active, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE FilmID = @id AND Durum = 0", Ct, ("@id", filmId)));
        Assert.Equal(available, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = @id", Ct, ("@id", filmId)));
    }

    /// <summary>
    /// Starts every operation at the same moment and returns, per operation, the exception it ended with
    /// or <see langword="null"/>. A database error other than a refusal fails the test.
    /// </summary>
    private static async Task<IReadOnlyList<Exception?>> RaceAsync(IReadOnlyList<Func<Task>> operations)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = operations
            .Select(operation => Task.Run(
                async () =>
                {
                    start.Wait(Ct);
                    try
                    {
                        await operation();
                        return (Exception?)null;
                    }
                    catch (Exception ex) when (ex is BusinessRuleException)
                    {
                        return ex;
                    }
                },
                Ct))
            .ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);
        Assert.DoesNotContain(results, r => r is SqlException);
        return results;
    }
}
