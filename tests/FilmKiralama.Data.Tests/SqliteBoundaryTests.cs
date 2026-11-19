using FilmKiralama.Business;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// Boundaries of the critical paths on the real services and SQLite: the shelf at zero, one and every
/// copy, the last copy, the first late day, the longest rental, the end of a lock and refused input
/// that must not write anything.
/// </summary>
public sealed class SqliteBoundaryTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>
    /// A film with two copies goes from a full shelf to one copy, to none (out of the catalog, the next
    /// customer is refused and nothing is written) and back to a full shelf; a return never puts more
    /// copies on the shelf than the shop owns.
    /// </summary>
    [Fact]
    public async Task Shelf_FullOneAndZero_FollowEveryRentalAndReturn()
    {
        var filmId = await AddFilmAsync(_db.Services, "Iki Kopya", copies: 2);
        var first = await RegisterAsync(_db.Services, "birinci", 1);
        var second = await RegisterAsync(_db.Services, "ikinci", 2);
        var third = await RegisterAsync(_db.Services, "ucuncu", 3);
        var films = _db.Get<IFilmService>();
        var rentals = _db.Get<IRentalService>();

        Assert.Equal((2, 2), await ShelfAsync(filmId));
        Assert.Contains(await films.GetCatalogAsync(third, FilmFilter.None, Ct), f => f.Id == filmId);

        var a = await RentAsync(_db.Services, first, filmId);
        Assert.Equal((1, 2), await ShelfAsync(filmId));
        Assert.Contains(await films.GetCatalogAsync(third, FilmFilter.None, Ct), f => f.Id == filmId);

        var b = await RentAsync(_db.Services, second, filmId);
        Assert.Equal((0, 2), await ShelfAsync(filmId));
        Assert.DoesNotContain(await films.GetCatalogAsync(third, FilmFilter.None, Ct), f => f.Id == filmId);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(third, rentals.Quote(filmId, 10m, 2), Ct));
        Assert.Equal(RentalService.NotAvailable, refused.Message);
        Assert.Equal((0, 2), await ShelfAsync(filmId));
        Assert.Equal(2, await RentalCountAsync(filmId));

        await rentals.ReturnAsync(first, a.RentalId, Ct);
        Assert.Equal((1, 2), await ShelfAsync(filmId));
        await rentals.ReturnAsync(second, b.RentalId, Ct);
        Assert.Equal((2, 2), await ShelfAsync(filmId));

        await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.ReturnAsync(second, b.RentalId, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.ReturnAsync(Admin(SeedAdminId), a.RentalId, Ct));
        Assert.Equal((2, 2), await ShelfAsync(filmId));
    }

    /// <summary>The single copy goes to the first customer; once it is back, the next one can rent it.</summary>
    [Fact]
    public async Task LastCopy_IsRentedOnceAndAvailableAgainAfterTheReturn()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var rentals = _db.Get<IRentalService>();
        var receipt = await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(_db.Services, other, SingleCopyFilmId));
        await rentals.ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);
        await RentAsync(_db.Services, other, SingleCopyFilmId);

        Assert.Equal((0, 1), await ShelfAsync(SingleCopyFilmId));
        Assert.Single(await rentals.GetMyActiveRentalsAsync(other, Ct));
        Assert.Empty(await rentals.GetMyActiveRentalsAsync(User(SeedUserId), Ct));
    }

    /// <summary>Returned on the due day nothing is owed; one day later exactly one day's late fee.</summary>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(10, 7)]
    public async Task LateFee_StartsTheDayAfterTheDueDay(int daysUntilReturn, int overdueDays)
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId, days: 3);
        _db.Clock.Advance(TimeSpan.FromDays(daysUntilReturn));

        var returned = await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);

        Assert.Equal(overdueDays, returned.OverdueDays);
        Assert.Equal(overdueDays * DailyLateFee, returned.LateFee);
        await using var db = _db.CreateContext();
        var stored = await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct);
        Assert.Equal(overdueDays == 0 ? null : overdueDays * DailyLateFee, stored.LateFee);
        Assert.Equal(Today.AddDays(daysUntilReturn), stored.ReturnedOn);
    }

    /// <summary>The longest rental the settings allow is accepted; one day more is refused before anything is written.</summary>
    [Fact]
    public async Task RentalLength_IsLimitedByTheSettings()
    {
        var rentals = _db.Get<IRentalService>();

        var error = Assert.Throws<BusinessRuleException>(() => rentals.Quote(ThreeCopiesFilmId, 25m, rentals.Policy.MaxRentalDays + 1));
        var longest = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId, days: rentals.Policy.MaxRentalDays);

        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", error.Message);
        Assert.Equal(Today.AddDays(30), longest.DueOn);
        Assert.Equal(1, await RentalCountAsync(ThreeCopiesFilmId));
    }

    /// <summary>
    /// After the fifth failure the account stays locked, also for the right password, until exactly the
    /// end of the fifteen minutes; refusals during the lock neither extend nor shorten it.
    /// </summary>
    [Fact]
    public async Task Lock_LastsExactlyFifteenMinutes()
    {
        var account = await RegisterAsync(_db.Services, "kilit", 1);
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("kilit", "Yanlis.Sifre1", Ct));
        }

        var lockedUntil = await LockoutEndAsync(account.UserId);
        Assert.Equal(Start.UtcDateTime + LoginPolicy.LockoutDuration, lockedUntil);

        _db.Clock.Advance(LoginPolicy.LockoutDuration - TimeSpan.FromSeconds(1));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("kilit", "Yanlis.Sifre1", Ct));
        Assert.Equal(lockedUntil, await LockoutEndAsync(account.UserId));

        _db.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(account.UserId, (await auth.SignInAsync("kilit", Password, Ct)).UserId);
    }

    /// <summary>Four failures do not lock; a success clears the count, so four more do not lock either.</summary>
    [Fact]
    public async Task Lock_NeedsFiveFailuresInARow()
    {
        await RegisterAsync(_db.Services, "dikkatli", 1);
        var auth = _db.Get<IAuthService>();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
            {
                await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("dikkatli", "Yanlis.Sifre1", Ct));
            }

            await auth.SignInAsync("dikkatli", Password, Ct);
        }
    }

    /// <summary>Refused input of every service writes nothing.</summary>
    [Fact]
    public async Task RefusedInput_WritesNothing()
    {
        var auth = _db.Get<IAuthService>();
        var films = _db.Get<IFilmService>();
        var accounts = _db.Get<IAccountService>();
        var setup = _db.Get<ISetupService>();
        int users, filmCount, rentalCount;
        await using (var before = _db.CreateContext())
        {
            (users, filmCount, rentalCount) = (await before.Users.CountAsync(Ct), await before.Films.CountAsync(Ct), await before.Rentals.CountAsync(Ct));
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("yeni", "kisa", "yeni@example.com", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("admin", Password, "yeni@example.com", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("yeni", Password, "eposta", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.AddAsync(Admin(SeedAdminId), new AccountInput("yeni", Password, "yeni@example.com", "555", UserRole.User), Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", Password, "Baska.Sifre1", Ct));
        var film = await Assert.ThrowsAsync<BusinessRuleException>(() => films.AddAsync(
            Admin(SeedAdminId),
            new FilmInput("Yeni", "Dram", "2099", "Ece Deneme", "0", "file:///C:/afis.png", "Deneme.", 0, true),
            Ct));
        Assert.Equal([nameof(FilmInput.Year), nameof(FilmInput.DailyPrice), nameof(FilmInput.ImageUrl), nameof(FilmInput.Copies)], film.FieldErrors.Keys);
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.ChangePasswordAsync(User(SeedUserId), "Demo.Kullanici1", "kisa", "kisa", Ct));

        await using var after = _db.CreateContext();
        Assert.Equal(users, await after.Users.CountAsync(Ct));
        Assert.Equal(filmCount, await after.Films.CountAsync(Ct));
        Assert.Equal(rentalCount, await after.Rentals.CountAsync(Ct));
        await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);
    }

    private async Task<(int Available, int Total)> ShelfAsync(int filmId)
    {
        await using var db = _db.CreateContext();
        var film = await db.Films.SingleAsync(f => f.Id == filmId, Ct);
        return (film.AvailableCopies, film.TotalCopies);
    }

    private async Task<int> RentalCountAsync(int filmId)
    {
        await using var db = _db.CreateContext();
        return await db.Rentals.CountAsync(r => r.FilmId == filmId, Ct);
    }

    private async Task<DateTime?> LockoutEndAsync(int userId)
    {
        await using var db = _db.CreateContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.LockoutEnd).SingleAsync(Ct);
    }
}
