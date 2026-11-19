using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>Watchlists with the real services and repositories on SQLite.</summary>
public sealed class SqliteWatchlistTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IWatchlistService Watchlist => _db.Get<IWatchlistService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Add_SameFilmTwice_KeepsOneEntry()
    {
        var customer = User(SeedUserId);

        Assert.True(await Watchlist.AddAsync(customer, ThreeCopiesFilmId, Ct));
        Assert.False(await Watchlist.AddAsync(customer, ThreeCopiesFilmId, Ct));

        Assert.Equal([ThreeCopiesFilmId], (await Watchlist.GetMineAsync(customer, Ct)).Select(e => e.Item.Film.Id));
    }

    [Fact]
    public async Task EmptyList_IsEmpty()
    {
        Assert.Empty(await Watchlist.GetMineAsync(User(SeedUserId), Ct));
    }

    /// <summary>Another account's list is out of reach: its entries are neither shown nor removable, and the answer is that of a missing entry.</summary>
    [Fact]
    public async Task AnotherAccountsList_IsTheSameAsNone()
    {
        var owner = User(SeedUserId);
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await Watchlist.AddAsync(owner, ThreeCopiesFilmId, Ct);
        var entry = (await Watchlist.GetMineAsync(owner, Ct)).Single().EntryId;

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => Watchlist.RemoveAsync(other, entry, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => Watchlist.RemoveAsync(other, entry + 1000, Ct));

        Assert.Equal(missing.Message, foreign.Message);
        Assert.Empty(await Watchlist.GetMineAsync(other, Ct));
        Assert.Single(await Watchlist.GetMineAsync(owner, Ct));

        await Watchlist.RemoveAsync(owner, entry, Ct);
        Assert.Empty(await Watchlist.GetMineAsync(owner, Ct));
    }

    /// <summary>Renting a film from the list takes it off the renter's list only.</summary>
    [Fact]
    public async Task Rent_TakesTheFilmOffTheRentersListOnly()
    {
        var renter = User(SeedUserId);
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await Watchlist.AddAsync(renter, ThreeCopiesFilmId, Ct);
        await Watchlist.AddAsync(renter, SingleCopyFilmId, Ct);
        await Watchlist.AddAsync(other, ThreeCopiesFilmId, Ct);

        await RentAsync(_db.Services, renter, ThreeCopiesFilmId);

        Assert.Equal([SingleCopyFilmId], (await Watchlist.GetMineAsync(renter, Ct)).Select(e => e.Item.Film.Id));
        Assert.Single(await Watchlist.GetMineAsync(other, Ct));
    }

    /// <summary>A film nobody can rent now stays on the list, marked as not available; an unlisted film cannot be added.</summary>
    [Fact]
    public async Task Availability_IsShownAndUnlistedFilmsCannotBeAdded()
    {
        var customer = User(SeedUserId);
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await Watchlist.AddAsync(customer, SingleCopyFilmId, Ct);
        await RentAsync(_db.Services, other, SingleCopyFilmId);
        var hidden = await AddFilmAsync(_db.Services, "Gizli", 1);
        await _db.Get<IFilmService>().UpdateAsync(Admin(SeedAdminId), hidden, new FilmInput("Gizli", "Dram", "2020", "Ece Deneme", "10", null, "Deneme filmi.", 1, false), Ct);

        var entry = Assert.Single(await Watchlist.GetMineAsync(customer, Ct));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Watchlist.AddAsync(customer, hidden, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => Watchlist.AddAsync(customer, 9999, Ct));

        Assert.False(entry.Item.IsAvailable);
        Assert.Equal(WatchlistService.FilmNotFound, error.Message);
        Assert.Equal(error.Message, missing.Message);
    }

    /// <summary>The hundredth film fits, the one after it does not.</summary>
    [Fact]
    public async Task Add_BeyondTheLimit_IsRefused()
    {
        var customer = User(SeedUserId);
        await using (var db = _db.CreateContext())
        {
            for (var i = 0; i <= WatchlistPolicy.MaxEntries; i++)
            {
                db.Films.Add(new Film { Title = "Liste " + i, Genre = "Dram", Year = 2020, Director = "Ece", DailyPrice = 5m, TotalCopies = 1, AvailableCopies = 1 });
            }

            await db.SaveChangesAsync(Ct);
        }

        var ids = await ListFilmIdsAsync();
        foreach (var id in ids.Take(WatchlistPolicy.MaxEntries - 1))
        {
            await using var db = _db.CreateContext();
            db.WatchlistEntries.Add(new WatchlistEntry { UserId = customer.UserId, FilmId = id, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        Assert.True(await Watchlist.AddAsync(customer, ids[WatchlistPolicy.MaxEntries - 1], Ct));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Watchlist.AddAsync(customer, ids[WatchlistPolicy.MaxEntries], Ct));

        Assert.Equal(WatchlistService.ListFull, error.Message);
        Assert.Equal(WatchlistPolicy.MaxEntries, (await Watchlist.GetMineAsync(customer, Ct)).Count);
    }

    /// <summary>A film that was never rented, and an account without rentals, can still be deleted; their entries go with them.</summary>
    [Fact]
    public async Task Delete_FilmOrAccount_TakesItsEntriesWithIt()
    {
        var customer = await RegisterAsync(_db.Services, "giden", 1);
        var film = await AddFilmAsync(_db.Services, "Silinecek", 1);
        await Watchlist.AddAsync(customer, film, Ct);
        await Watchlist.AddAsync(customer, ThreeCopiesFilmId, Ct);
        await Watchlist.AddAsync(User(SeedUserId), film, Ct);

        await _db.Get<IFilmService>().DeleteAsync(Admin(SeedAdminId), film, Ct);
        Assert.Equal([ThreeCopiesFilmId], (await Watchlist.GetMineAsync(customer, Ct)).Select(e => e.Item.Film.Id));

        await _db.Get<IAccountService>().DeleteAsync(Admin(SeedAdminId), customer.UserId, Ct);
        await using var db = _db.CreateContext();
        Assert.Equal(0, await db.WatchlistEntries.CountAsync(Ct));
    }

    private async Task<List<int>> ListFilmIdsAsync()
    {
        await using var db = _db.CreateContext();
        return await db.Films.Where(f => f.Title.StartsWith("Liste ")).OrderBy(f => f.Id).Select(f => f.Id).ToListAsync(Ct);
    }
}
