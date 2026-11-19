using System.Globalization;
using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Repositories;
using FilmKiralama.Domain.Entities;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>The catalog search with the real services and repositories on an empty SQLite database.</summary>
public sealed class SqliteCatalogTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new(demo: false);
    private UserSession _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IFilmService Films => _db.Get<IFilmService>();

    public async ValueTask InitializeAsync()
    {
        await _db.InitializeAsync();
        _admin = await _db.Get<ISetupService>().CreateFirstAdminAsync(null, "yonetici", Password, Password, Ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>%, _, [ and the escape character in a search match only themselves.</summary>
    [Theory]
    [InlineData("100%", "100% Gerçek")]
    [InlineData("_", "Alt_Çizgi")]
    [InlineData("[K", "[Köşeli] Film")]
    [InlineData("\\", "Ters\\Bölü")]
    [InlineData("%_", null)]
    public async Task Search_LikeCharacters_MatchOnlyThemselves(string term, string? expected)
    {
        foreach (var title in new[] { "100% Gerçek", "Tam 1000 Film", "Alt_Çizgi", "AltXÇizgi", "[Köşeli] Film", "Köşesiz Film", "Ters\\Bölü", "TersBölü" })
        {
            await AddAsync(title);
        }

        var page = await Films.SearchCatalogAsync(User(1), new CatalogQuery(Title: term), Ct);

        Assert.Equal(expected is null ? [] : [expected], page.Items.Select(i => i.Film.Title));
    }

    [Fact]
    public void EscapeLike_EscapesTheEscapeCharacterFirst()
    {
        Assert.Equal("a\\\\b\\%c\\_d\\[e]", FilmRepository.EscapeLike("a\\b%c_d[e]"));
    }

    /// <summary>
    /// Films with equal titles come in id order, so walking every page gives each film exactly once in
    /// the same order as one big page; a page past the end gives the last page.
    /// </summary>
    [Fact]
    public async Task Pages_AreStableAndCoverEveryFilmOnce()
    {
        var ids = new List<int>();
        for (var i = 0; i < 23; i++)
        {
            ids.Add(await AddAsync(i % 3 == 0 ? "Aynı Ad" : "Film " + (i % 7).ToString(CultureInfo.InvariantCulture)));
        }

        var all = await Films.SearchCatalogAsync(User(1), new CatalogQuery(PageSize: 50), Ct);
        var walked = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Films.SearchCatalogAsync(User(1), new CatalogQuery(Page: page, PageSize: 10), Ct);
            Assert.Equal((23, 3, page), (result.TotalCount, result.PageCount, result.Page));
            walked.AddRange(result.Items.Select(i => i.Film.Id));
        }

        Assert.Equal(all.Items.Select(i => i.Film.Id), walked);
        Assert.Equal(ids.Order(), walked.Order());
        Assert.Equal(all.Items.OrderBy(i => i.Film.Title, StringComparer.Ordinal).ThenBy(i => i.Film.Id).Select(i => i.Film.Id), walked);

        var beyond = await Films.SearchCatalogAsync(User(1), new CatalogQuery(Page: 99, PageSize: 10), Ct);
        Assert.Equal(3, beyond.Page);
        Assert.Equal(walked.Skip(20), beyond.Items.Select(i => i.Film.Id));
    }

    [Fact]
    public async Task Search_NothingFound_IsOneEmptyPage()
    {
        var page = await Films.SearchCatalogAsync(User(1), new CatalogQuery(Title: "yok", Page: 5), Ct);

        Assert.Empty(page.Items);
        Assert.Equal((0, 1, 1), (page.TotalCount, page.Page, page.PageCount));
    }

    [Fact]
    public async Task Search_YearRangeAndGenre_IncludeBothEnds()
    {
        await AddAsync("Eski", year: 1999);
        await AddAsync("Başlangıç", year: 2000);
        await AddAsync("Bitiş", year: 2005, genre: "Komedi");
        await AddAsync("Yeni", year: 2006);

        var range = await Films.SearchCatalogAsync(User(1), new CatalogQuery(YearFrom: 2000, YearTo: 2005), Ct);
        var genre = await Films.SearchCatalogAsync(User(1), new CatalogQuery(Genre: "Komedi", YearFrom: 2000), Ct);

        Assert.Equal(["Başlangıç", "Bitiş"], range.Items.Select(i => i.Film.Title));
        Assert.Equal(["Bitiş"], genre.Items.Select(i => i.Film.Title));
    }

    /// <summary>Customers never see unlisted films; "available only" also leaves out films without a copy on the shelf.</summary>
    [Fact]
    public async Task Search_ListedAndAvailable_AreDecidedPerScreen()
    {
        await AddAsync("Rafta");
        var gone = await AddAsync("Son Kopya", copies: 1);
        await AddAsync("Kapalı", listed: false);
        await RentAsync(_db.Services, await RegisterAsync(_db.Services, "kiraci", 1), gone);

        var catalog = await Films.SearchCatalogAsync(User(1), new CatalogQuery(), Ct);
        var available = await Films.SearchCatalogAsync(User(1), new CatalogQuery(AvailableOnly: true), Ct);
        var admin = await Films.SearchAllAsync(_admin, new CatalogQuery(), Ct);

        Assert.Equal(["Rafta", "Son Kopya"], catalog.Items.Select(i => i.Film.Title));
        Assert.Equal([true, false], catalog.Items.Select(i => i.IsAvailable));
        Assert.Equal(["Rafta"], available.Items.Select(i => i.Film.Title));
        Assert.Equal(["Kapalı", "Rafta", "Son Kopya"], admin.Items.Select(i => i.Film.Title));
    }

    /// <summary>
    /// The minimum rating is compared with the shown average: 3.95 shows as 4.0 and counts as four stars,
    /// 3.9 does not; hidden ratings do not count.
    /// </summary>
    [Fact]
    public async Task Search_MinimumRating_FollowsTheShownAverage()
    {
        var almost = await AddAsync("Neredeyse Dört");
        var below = await AddAsync("Üç Dokuz");
        var thirds = await AddAsync("Üçte Bir");
        var hidden = await AddAsync("Gizli Puanlı");
        await RateAsync(almost, [.. Enumerable.Repeat(4, 19), 3]);
        await RateAsync(below, [.. Enumerable.Repeat(4, 18), 3, 3]);
        await RateAsync(thirds, [4, 4, 3]);
        await RateAsync(hidden, [5], hiddenRatings: [1, 1, 1]);

        var four = await Films.SearchCatalogAsync(User(1), new CatalogQuery(MinRating: 4), Ct);
        var three = await Films.SearchCatalogAsync(User(1), new CatalogQuery(MinRating: 3), Ct);
        var five = await Films.SearchCatalogAsync(User(1), new CatalogQuery(MinRating: 5), Ct);

        Assert.Equal(["Gizli Puanlı", "Neredeyse Dört"], four.Items.Select(i => i.Film.Title));
        Assert.Equal([5.0m, 4.0m], four.Items.Select(i => i.AverageRating));
        Assert.Equal(["Gizli Puanlı", "Neredeyse Dört", "Üç Dokuz", "Üçte Bir"], three.Items.Select(i => i.Film.Title));
        Assert.Equal([5.0m, 4.0m, 3.9m, 3.7m], three.Items.Select(i => i.AverageRating));
        Assert.Equal([1, 20, 20, 3], three.Items.Select(i => i.RatingCount));
        Assert.Equal(["Gizli Puanlı"], five.Items.Select(i => i.Film.Title));
    }

    [Fact]
    public async Task SearchAll_ByADemotedAdmin_IsRefused()
    {
        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Films.SearchAllAsync(Admin(_admin.UserId + 100), new CatalogQuery(), Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
    }

    private Task<int> AddAsync(string title, int year = 2020, string genre = "Dram", int copies = 2, bool listed = true) =>
        Films.AddAsync(_admin, new FilmInput(title, genre, year.ToString(CultureInfo.InvariantCulture), "Ece Deneme", "10", null, "Deneme filmi.", copies, listed), Ct);

    /// <summary>Stores visible (and hidden) ratings of new accounts directly, as reviews of returned rentals would be.</summary>
    private async Task RateAsync(int filmId, int[] ratings, int[]? hiddenRatings = null)
    {
        await using var db = _db.CreateContext();
        foreach (var (rating, hidden) in ratings.Select(r => (r, false)).Concat((hiddenRatings ?? []).Select(r => (r, true))))
        {
            var user = new User { UserName = "puanlayan" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
            db.Users.Add(user);
            await db.SaveChangesAsync(Ct);
            db.Reviews.Add(new Review { UserId = user.Id, FilmId = filmId, Rating = rating, IsHidden = hidden, UpdatedAt = DateTime.UtcNow });
        }

        await db.SaveChangesAsync(Ct);
    }
}
