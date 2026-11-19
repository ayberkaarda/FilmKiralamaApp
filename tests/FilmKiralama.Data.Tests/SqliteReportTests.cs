using FilmKiralama.Business;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>Reports and recommendations with the real services and repositories on SQLite.</summary>
public sealed class SqliteReportTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IReportService Reports => _db.Get<IReportService>();

    private IRentalService Rentals => _db.Get<IRentalService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>Rentals started on the first and the last day count; those of the day before and the day after do not.</summary>
    [Fact]
    public async Task MostRented_CountsBothEndsOfThePeriodOnly()
    {
        var customer = User(SeedUserId);
        foreach (var day in new[] { 0, 1, 2, 3, 4 })
        {
            var rental = await RentAsync(_db.Services, customer, ThreeCopiesFilmId, days: 1);
            await Rentals.ReturnAsync(customer, rental.RentalId, Ct);
            if (day is 1 or 3)
            {
                var other = await RentAsync(_db.Services, customer, SingleCopyFilmId, days: 1);
                await Rentals.ReturnAsync(customer, other.RentalId, Ct);
            }

            _db.Clock.Advance(TimeSpan.FromDays(1));
        }

        var rows = await Reports.GetMostRentedAsync(Admin(SeedAdminId), new ReportRange(Today.AddDays(1), Today.AddDays(3)), Ct);

        Assert.Equal([(ThreeCopiesFilmId, 3), (SingleCopyFilmId, 2)], rows.Select(r => (r.FilmId, r.Rentals)));
        Assert.Empty(await Reports.GetMostRentedAsync(Admin(SeedAdminId), new ReportRange(Today.AddDays(10), Today.AddDays(20)), Ct));
    }

    /// <summary>A copy due yesterday is overdue, one due today is not; the late fees of a period add up to their total.</summary>
    [Fact]
    public async Task OverdueAndLateFees_FollowTheDueDays()
    {
        var customer = User(SeedUserId);
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var late = await RentAsync(_db.Services, customer, ThreeCopiesFilmId, days: 2);
        var onTime = await RentAsync(_db.Services, other, ThreeCopiesFilmId, days: 3);
        var lateAndReturned = await RentAsync(_db.Services, other, SingleCopyFilmId, days: 1);

        _db.Clock.Advance(TimeSpan.FromDays(3));
        var overdue = await Reports.GetOverdueAsync(Admin(SeedAdminId), Ct);

        Assert.Equal([late.RentalId, lateAndReturned.RentalId], overdue.Select(r => r.Rental.RentalId).Order());
        Assert.DoesNotContain(overdue, r => r.Rental.RentalId == onTime.RentalId);
        Assert.Equal(1, overdue.Single(r => r.Rental.RentalId == late.RentalId).OverdueDays);
        Assert.Equal(2 * DailyLateFee, overdue.Single(r => r.Rental.RentalId == lateAndReturned.RentalId).AccruedFee);

        await Rentals.ReturnAsync(other, lateAndReturned.RentalId, Ct);
        await Rentals.ReturnAsync(other, onTime.RentalId, Ct);
        _db.Clock.Advance(TimeSpan.FromDays(1));
        await Rentals.ReturnAsync(customer, late.RentalId, Ct);

        var fees = await Reports.GetLateFeesAsync(Admin(SeedAdminId), new ReportRange(Today, Today.AddDays(4)), Ct);
        Assert.Equal([lateAndReturned.RentalId, late.RentalId], fees.Rows.Select(r => r.RentalId));
        Assert.Equal(2 * DailyLateFee + 2 * DailyLateFee, fees.Total);
        Assert.Empty(await Reports.GetOverdueAsync(Admin(SeedAdminId), Ct));
        Assert.Empty((await Reports.GetLateFeesAsync(Admin(SeedAdminId), new ReportRange(Today, Today.AddDays(2)), Ct)).Rows);
    }

    [Fact]
    public async Task Reports_ForADemotedAdmin_AreRefused()
    {
        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Reports.GetOverdueAsync(Admin(SeedUserId), Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
    }

    /// <summary>
    /// A customer who rented two dramas and one comedy gets the dramas available now that they have not
    /// rented; a film without a copy, an unlisted one and the dramas already rented are left out.
    /// </summary>
    [Fact]
    public async Task Recommendations_AreTheAvailableUnrentedFilmsOfTheGenreRentedMost()
    {
        var films = _db.Get<IFilmService>();
        var admin = Admin(SeedAdminId);
        var customer = await RegisterAsync(_db.Services, "izleyici", 1);
        var other = await RegisterAsync(_db.Services, "baska", 2);
        Assert.Empty(await _db.Get<IRecommendationService>().GetForMeAsync(customer, Ct));

        var dramaA = await Add(films, admin, "Dram A", "Dram");
        var dramaB = await Add(films, admin, "Dram B", "Dram");
        var comedy = await Add(films, admin, "Komedi A", "Komedi");
        var dramaNew = await Add(films, admin, "Dram Yeni", "Dram");
        var dramaGone = await Add(films, admin, "Dram Tükendi", "Dram", copies: 1);
        await Add(films, admin, "Dram Kapalı", "Dram", listed: false);
        await Add(films, admin, "Komedi Yeni", "Komedi");
        foreach (var filmId in new[] { dramaA, dramaB, comedy })
        {
            var rental = await RentAsync(_db.Services, customer, filmId);
            await Rentals.ReturnAsync(customer, rental.RentalId, Ct);
        }

        await RentAsync(_db.Services, other, dramaGone);

        var recommended = await _db.Get<IRecommendationService>().GetForMeAsync(customer, Ct);

        Assert.Contains(recommended, f => f.Film.Id == dramaNew);
        Assert.All(recommended, f => Assert.Equal("Dram", f.Film.Genre));
        Assert.DoesNotContain(recommended, f => f.Film.Id is var id && (id == dramaA || id == dramaB || id == dramaGone));
        Assert.DoesNotContain(recommended, f => f.Film.Title == "Dram Kapalı");
        Assert.All(recommended, f => Assert.True(f.IsAvailable));
    }

    private static Task<int> Add(IFilmService films, FilmKiralama.Business.Services.UserSession admin, string title, string genre, int copies = 2, bool listed = true) =>
        films.AddAsync(admin, new FilmInput(title, genre, "2020", "Ece Deneme", "10", null, "Deneme filmi.", copies, listed), Ct);
}
