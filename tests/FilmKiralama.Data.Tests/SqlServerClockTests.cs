using FilmKiralama.Business;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// Two application instances whose computer clocks are wrong in opposite directions, on a real SQL
/// Server instance (see <see cref="SqlServerTestDatabase"/>): the lock and the rental days follow the
/// database's clock, so both instances agree.
/// </summary>
public sealed class SqlServerClockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A computer far behind locks an account; a computer a day ahead must still see the lock, which ends
    /// fifteen minutes after the database's now.
    /// </summary>
    [Fact]
    public async Task Lock_SetByAComputerBehind_HoldsForAComputerAhead()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var behind = database.BuildServices();
        await behind.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var ahead = database.BuildServices(s => s.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1))));
        await RegisterAsync(behind, "saat", 1);

        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => behind.GetRequiredService<IAuthService>().SignInAsync("saat", "Yanlis.Sifre1", Ct));
        }

        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'saat' AND KilitBitis > DATEADD(MINUTE, 14, SYSUTCDATETIME()) AND KilitBitis <= DATEADD(MINUTE, 15, SYSUTCDATETIME())",
            Ct));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ahead.GetRequiredService<IAuthService>().SignInAsync("saat", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
    }

    /// <summary>
    /// A rental made on a computer months behind starts on the database's day, and a return on a computer
    /// a day ahead is charged for the days the database counts.
    /// </summary>
    [Fact]
    public async Task RentalDays_FollowTheDatabaseClockOnEveryComputer()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var behind = database.BuildServices(demo: true);
        await behind.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var ahead = database.BuildServices(s => s.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1))));
        var customer = await RegisterAsync(behind, "kiraci", 1);

        var receipt = await RentAsync(behind, customer, ThreeCopiesFilmId, days: 2);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = @id AND KiralamaTarihi = CAST(SYSUTCDATETIME() AS date) AND PlanlananIadeTarihi = DATEADD(DAY, 2, CAST(SYSUTCDATETIME() AS date))",
            Ct,
            ("@id", receipt.RentalId)));

        await database.ExecuteAsync(
            "UPDATE dbo.Kiralama SET KiralamaTarihi = DATEADD(DAY, -6, CAST(SYSUTCDATETIME() AS date)), PlanlananIadeTarihi = DATEADD(DAY, -4, CAST(SYSUTCDATETIME() AS date)) WHERE KiralamaID = @id",
            Ct,
            ("@id", receipt.RentalId));
        var returned = await ahead.GetRequiredService<IRentalService>().ReturnAsync(customer, receipt.RentalId, Ct);

        Assert.Equal(4, returned.OverdueDays);
        Assert.Equal(4 * DailyLateFee, returned.LateFee);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = @id AND GercekIadeTarihi = CAST(SYSUTCDATETIME() AS date)",
            Ct,
            ("@id", receipt.RentalId)));
    }
}
