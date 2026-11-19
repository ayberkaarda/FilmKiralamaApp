using FilmKiralama.Business;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>Ratings and comments with the real services and repositories on SQLite.</summary>
public sealed class SqliteReviewTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IReviewService Reviews => _db.Get<IReviewService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>A film still out, or never rented, cannot be rated; once it is returned it can.</summary>
    [Fact]
    public async Task Rate_BeforeTheReturn_IsRefusedAndAfterItAllowed()
    {
        var customer = User(SeedUserId);
        var never = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(5, null), Ct));
        var rental = await RentAsync(_db.Services, customer, ThreeCopiesFilmId);
        var stillOut = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(5, null), Ct));

        Assert.Equal(ReviewService.NotEligible, never.Message);
        Assert.Equal(ReviewService.NotEligible, stillOut.Message);
        Assert.Equal(0, await CountAsync());

        await _db.Get<IRentalService>().ReturnAsync(customer, rental.RentalId, Ct);
        await Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(5, "Çok iyi"), Ct);

        var own = await Reviews.GetOwnAsync(customer, ThreeCopiesFilmId, Ct);
        Assert.Equal((5, "Çok iyi", false), (own!.Rating, own.Comment, own.IsHidden));
        Assert.Equal([ThreeCopiesFilmId], (await Reviews.GetReviewableAsync(customer, Ct)).Select(f => f.FilmId));
    }

    /// <summary>A second save changes the one review; a review an admin hid stays hidden when it is changed.</summary>
    [Fact]
    public async Task Rate_Again_ChangesTheSameReviewAndKeepsItHidden()
    {
        var customer = await WatchedAsync("izleyici", 1, ThreeCopiesFilmId);
        await Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(2, "Sıkıcı"), Ct);
        var first = await Reviews.GetOwnAsync(customer, ThreeCopiesFilmId, Ct);
        await Reviews.SetHiddenAsync(Admin(SeedAdminId), first!.Id, true, Ct);

        _db.Clock.Advance(TimeSpan.FromDays(1));
        await Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(4, "Tekrar izledim"), Ct);

        var own = await Reviews.GetOwnAsync(customer, ThreeCopiesFilmId, Ct);
        Assert.Equal(first.Id, own!.Id);
        Assert.Equal((4, "Tekrar izledim", true), (own.Rating, own.Comment, own.IsHidden));
        Assert.Equal(1, await CountAsync());
    }

    /// <summary>Hidden reviews are left out of the film's reviews and of its average.</summary>
    [Fact]
    public async Task FilmReviews_LeaveOutHiddenOnesAndAverageTheRest()
    {
        var a = await WatchedAsync("birinci", 1, ThreeCopiesFilmId);
        var b = await WatchedAsync("ikinci", 2, ThreeCopiesFilmId);
        var c = await WatchedAsync("ucuncu", 3, ThreeCopiesFilmId);
        await Reviews.SaveAsync(a, ThreeCopiesFilmId, new ReviewInput(4, "İyi"), Ct);
        await Reviews.SaveAsync(b, ThreeCopiesFilmId, new ReviewInput(3, null), Ct);
        await Reviews.SaveAsync(c, ThreeCopiesFilmId, new ReviewInput(1, "Kaba bir yorum"), Ct);

        var all = await Reviews.GetFilmReviewsAsync(a, ThreeCopiesFilmId, Ct);
        Assert.Equal((2.7m, 3), (all.Average, all.Count));

        var rude = (await Reviews.GetAllAsync(Admin(SeedAdminId), hiddenOnly: false, 1, Ct)).Items.Single(r => r.Rating == 1);
        await Reviews.SetHiddenAsync(Admin(SeedAdminId), rude.Id, true, Ct);

        var visible = await Reviews.GetFilmReviewsAsync(a, ThreeCopiesFilmId, Ct);
        Assert.Equal((3.5m, 2), (visible.Average, visible.Count));
        Assert.DoesNotContain(visible.Reviews, r => r.Comment == "Kaba bir yorum");
        Assert.Equal([rude.Id], (await Reviews.GetAllAsync(Admin(SeedAdminId), hiddenOnly: true, 1, Ct)).Items.Select(r => r.Id));
        Assert.All(visible.Reviews, r => Assert.EndsWith("***", r.Author, StringComparison.Ordinal));
    }

    /// <summary>Deleting another account's review answers like a missing one and changes nothing.</summary>
    [Fact]
    public async Task DeleteOwn_AnotherAccountsReview_IsTheSameAsAMissingOne()
    {
        var owner = await WatchedAsync("sahip", 1, ThreeCopiesFilmId);
        var other = await RegisterAsync(_db.Services, "baska", 2);
        await Reviews.SaveAsync(owner, ThreeCopiesFilmId, new ReviewInput(5, null), Ct);
        var id = (await Reviews.GetOwnAsync(owner, ThreeCopiesFilmId, Ct))!.Id;

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.DeleteOwnAsync(other, id, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.DeleteOwnAsync(other, id + 1000, Ct));

        Assert.Equal(missing.Message, foreign.Message);
        Assert.Equal(ReviewService.ReviewNotFound, foreign.Message);
        Assert.Null(await Reviews.GetOwnAsync(other, ThreeCopiesFilmId, Ct));
        Assert.Equal(1, await CountAsync());

        await Reviews.DeleteOwnAsync(owner, id, Ct);
        Assert.Equal(0, await CountAsync());
    }

    /// <summary>
    /// Deleting a review an admin hid and writing it again does not make it public: the hidden mark
    /// outlives the deletion, the deleted text is gone and an admin cannot show the deleted review.
    /// </summary>
    [Fact]
    public async Task HiddenReview_DeletedAndWrittenAgain_StaysHidden()
    {
        var author = await WatchedAsync("kaba", 1, ThreeCopiesFilmId);
        var admin = Admin(SeedAdminId);
        await Reviews.SaveAsync(author, ThreeCopiesFilmId, new ReviewInput(1, "Kaba bir yorum"), Ct);
        var id = (await Reviews.GetOwnAsync(author, ThreeCopiesFilmId, Ct))!.Id;
        await Reviews.SetHiddenAsync(admin, id, true, Ct);

        await Reviews.DeleteOwnAsync(author, id, Ct);

        Assert.Null(await Reviews.GetOwnAsync(author, ThreeCopiesFilmId, Ct));
        Assert.Null((await Reviews.GetReviewableAsync(author, Ct)).Single().ReviewId);
        Assert.Empty((await Reviews.GetAllAsync(admin, hiddenOnly: false, 1, Ct)).Items);
        var show = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.SetHiddenAsync(admin, id, false, Ct));
        Assert.Equal(ReviewService.ReviewNotFound, show.Message);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => Reviews.DeleteOwnAsync(author, id, Ct));
        Assert.Equal(ReviewService.ReviewNotFound, again.Message);
        await using (var db = _db.CreateContext())
        {
            Assert.Null((await db.Reviews.SingleAsync(Ct)).Comment);
        }

        await Reviews.SaveAsync(author, ThreeCopiesFilmId, new ReviewInput(1, "Kaba bir yorum"), Ct);

        var own = await Reviews.GetOwnAsync(author, ThreeCopiesFilmId, Ct);
        Assert.Equal((1, "Kaba bir yorum", true), (own!.Rating, own.Comment, own.IsHidden));
        var film = await Reviews.GetFilmReviewsAsync(author, ThreeCopiesFilmId, Ct);
        Assert.Equal((0, null), (film.Count, film.Average));
        Assert.Empty(film.Reviews);
        Assert.Equal([id], (await Reviews.GetAllAsync(admin, hiddenOnly: true, 1, Ct)).Items.Select(r => r.Id));
        Assert.Equal(1, await CountAsync());
    }

    /// <summary>The database refuses ratings outside one to five and comments longer than its column on its own.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Database_RefusesRatingsOutsideOneToFive(int rating)
    {
        await WatchedAsync("dogrudan", 1, ThreeCopiesFilmId);
        await using var db = _db.CreateContext();
        var userId = await db.Users.Where(u => u.UserName == "dogrudan").Select(u => u.Id).SingleAsync(Ct);
        db.Reviews.Add(new Review { UserId = userId, FilmId = ThreeCopiesFilmId, Rating = rating, UpdatedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    /// <summary>A demoted admin's open session can no longer hide reviews.</summary>
    [Fact]
    public async Task Moderation_ByADemotedAdmin_IsRefusedAndChangesNothing()
    {
        var customer = await WatchedAsync("yazar", 1, ThreeCopiesFilmId);
        await Reviews.SaveAsync(customer, ThreeCopiesFilmId, new ReviewInput(5, null), Ct);
        var id = (await Reviews.GetOwnAsync(customer, ThreeCopiesFilmId, Ct))!.Id;

        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Reviews.SetHiddenAsync(Admin(SeedUserId), id, true, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        Assert.False((await Reviews.GetOwnAsync(customer, ThreeCopiesFilmId, Ct))!.IsHidden);
    }

    /// <summary>A new customer who rented and returned <paramref name="filmId"/>.</summary>
    private async Task<UserSession> WatchedAsync(string name, int number, int filmId)
    {
        var customer = await RegisterAsync(_db.Services, name, number);
        var rental = await RentAsync(_db.Services, customer, filmId);
        await _db.Get<IRentalService>().ReturnAsync(customer, rental.RentalId, Ct);
        return customer;
    }

    private async Task<int> CountAsync()
    {
        await using var db = _db.CreateContext();
        return await db.Reviews.CountAsync(Ct);
    }
}
