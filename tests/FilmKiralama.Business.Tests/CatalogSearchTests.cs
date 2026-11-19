using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>The catalog search: its limits are checked before the repository, and customers never reach unlisted films.</summary>
public sealed class CatalogSearchTests
{
    private readonly Mock<IFilmRepository> _films = new(MockBehavior.Strict);
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<CatalogQuery, string> Invalid => new()
    {
        { new CatalogQuery(Title: new string('a', 51)), CatalogQueryValidator.TitleTooLong },
        { new CatalogQuery(Title: "gizli" + (char)0x202E), CatalogQueryValidator.TitleInvalid },
        { new CatalogQuery(Genre: new string('a', 51)), CatalogQueryValidator.GenreTooLong },
        { new CatalogQuery(Genre: "Dram" + (char)0x200B), CatalogQueryValidator.GenreInvalid },
        { new CatalogQuery(YearFrom: 1899), CatalogQueryValidator.YearInvalid },
        { new CatalogQuery(YearTo: 10_000), CatalogQueryValidator.YearInvalid },
        { new CatalogQuery(YearFrom: 2001, YearTo: 2000), CatalogQueryValidator.YearsReversed },
        { new CatalogQuery(MinRating: 0), CatalogQueryValidator.RatingInvalid },
        { new CatalogQuery(MinRating: 6), CatalogQueryValidator.RatingInvalid },
        { new CatalogQuery(Page: 0), CatalogQueryValidator.PageInvalid },
        { new CatalogQuery(Page: -5), CatalogQueryValidator.PageInvalid },
        { new CatalogQuery(PageSize: 0), CatalogQueryValidator.PageSizeInvalid },
        { new CatalogQuery(PageSize: 51), CatalogQueryValidator.PageSizeInvalid },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Search_OutsideItsLimits_IsRefusedBeforeTheRepository(CatalogQuery query, string message)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SearchCatalogAsync(Sessions.User, query, Ct));

        Assert.Equal(message, error.Message);
        _films.VerifyNoOtherCalls();
    }

    public static TheoryData<CatalogQuery> AtTheLimits => new()
    {
        new CatalogQuery(Title: new string('a', 50)),
        new CatalogQuery(YearFrom: 1900, YearTo: 1900),
        new CatalogQuery(MinRating: 1),
        new CatalogQuery(MinRating: 5),
        new CatalogQuery(Page: 1, PageSize: 1),
        new CatalogQuery(Page: int.MaxValue, PageSize: 50),
    };

    [Theory]
    [MemberData(nameof(AtTheLimits))]
    public async Task Search_AtItsLimits_ReachesTheCustomersCatalogOnly(CatalogQuery query)
    {
        _films.Setup(f => f.SearchAsync(query, Sessions.User.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CatalogEntry>([], 0, 1, query.PageSize));

        var page = await Service().SearchCatalogAsync(Sessions.User, query, Ct);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.PageCount);
    }

    /// <summary>The title is trimmed and a blank one searches nothing; ratings arrive as averages.</summary>
    [Fact]
    public async Task Search_TrimsTheTitleAndAveragesTheRatings()
    {
        var film = new Film { Id = 3, Title = "Sisli Liman", IsListed = true, AvailableCopies = 0 };
        _films.Setup(f => f.SearchAsync(new CatalogQuery(Title: "Sisli", Genre: null), Sessions.User.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CatalogEntry>([new CatalogEntry(film, new RatingTotals(3, 11))], 13, 2, 12));
        _films.Setup(f => f.SearchAsync(new CatalogQuery(), Sessions.User.UserId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CatalogEntry>([], 0, 1, 12));

        var page = await Service().SearchCatalogAsync(Sessions.User, new CatalogQuery(Title: "  Sisli  ", Genre: " "), Ct);
        await Service().SearchCatalogAsync(Sessions.User, new CatalogQuery(Title: "   "), Ct);

        var item = Assert.Single(page.Items);
        Assert.Equal((3.7m, 3, false), (item.AverageRating, item.RatingCount, item.IsAvailable));
        Assert.Equal((2, 2, 13), (page.Page, page.PageCount, page.TotalCount));
    }

    [Fact]
    public async Task SearchAll_IsForAdminsAndChecksTheStoredRole()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().SearchAllAsync(Sessions.User, new CatalogQuery(), Ct));
        _films.VerifyNoOtherCalls();

        _films.Setup(f => f.SearchAsync(new CatalogQuery(), Sessions.Admin.UserId, true, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());
        var revoked = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().SearchAllAsync(Sessions.Admin, new CatalogQuery(), Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, revoked.Message);
        Assert.Equal(["access.denied user", "access.session_revoked admin"], _audit.Entries);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 12, 3)]
    public void PageCount_IsAtLeastOneAndCoversEveryRow(int total, int size, int pages)
    {
        Assert.Equal(pages, Paging.PageCount(total, size));
        Assert.Equal(pages, Paging.ClampPage(int.MaxValue, total, size));
        Assert.Equal(1, Paging.ClampPage(0, total, size));
    }

    private FilmService Service() =>
        new(_films.Object, new FakeTimeProvider(), _audit, TestValidators.Film, new CatalogQueryValidator());
}
