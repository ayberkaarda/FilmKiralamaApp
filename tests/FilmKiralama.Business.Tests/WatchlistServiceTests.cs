using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>The watchlist service with a strict mock: the session decides whose list is reached.</summary>
public sealed class WatchlistServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IWatchlistRepository> _watchlists = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_NewFilm_IsRecordedAndAFilmAlreadyListedIsNot()
    {
        _watchlists.SetupSequence(w => w.AddAsync(Sessions.User.Actor, 3, Now, WatchlistPolicy.MaxEntries, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WatchlistAddOutcome.Added)
            .ReturnsAsync(WatchlistAddOutcome.AlreadyListed);

        Assert.True(await Service().AddAsync(Sessions.User, 3, Ct));
        Assert.False(await Service().AddAsync(Sessions.User, 3, Ct));

        Assert.Equal(["watchlist.changed user"], _audit.Entries);
    }

    [Theory]
    [InlineData(WatchlistAddOutcome.FilmNotFound)]
    [InlineData(WatchlistAddOutcome.ListFull)]
    public async Task Add_Refused_IsARuleMessageWithoutAnEntry(WatchlistAddOutcome outcome)
    {
        _watchlists.Setup(w => w.AddAsync(Sessions.User.Actor, 3, Now, WatchlistPolicy.MaxEntries, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.User, 3, Ct));

        Assert.Equal(outcome == WatchlistAddOutcome.ListFull ? WatchlistService.ListFull : WatchlistService.FilmNotFound, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Remove_AnotherAccountsOrAMissingEntry_GetsOneAnswerAndNoEntry()
    {
        _watchlists.Setup(w => w.RemoveAsync(Sessions.User.Actor, 9, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RemoveAsync(Sessions.User, 9, Ct));

        Assert.Equal(WatchlistService.EntryNotFound, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task GetMine_ShowsAvailabilityAndTheAverage()
    {
        var film = new Film { Id = 3, Title = "Sisli Liman", IsListed = true, AvailableCopies = 1 };
        _watchlists.Setup(w => w.GetAsync(Sessions.User.Actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WatchlistItem(9, film, new RatingTotals(2, 7), Now.AddDays(-1))]);

        var entry = Assert.Single(await Service().GetMineAsync(Sessions.User, Ct));

        Assert.Equal((9, true, 3.5m, new DateOnly(2026, 3, 1)), (entry.EntryId, entry.Item.IsAvailable, entry.Item.AverageRating, entry.AddedOn));
    }

    [Fact]
    public async Task Calls_ForAnAccountThatIsGone_AskToSignInAgain()
    {
        _watchlists.Setup(w => w.GetAsync(Sessions.User.Actor, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());

        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetMineAsync(Sessions.User, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        Assert.Equal(["access.session_revoked user"], _audit.Entries);
    }

    private WatchlistService Service() => new(_watchlists.Object, new TimeProviderClock(_time), _time, _audit);
}
