using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

public sealed class FilmServiceTests
{
    private readonly Mock<IFilmRepository> _films = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FilmInput Valid => new("Sisli Liman", "Gerilim", "2019", "Ada Örnek", "25,50", "https://example.com/afis.png", "Bir hikâye.", 3, true);

    [Fact]
    public async Task AdminOperations_ForAUser_AreDeniedBeforeAnyRepositoryCall()
    {
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.GetAllAsync(Sessions.User, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.User, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.User, 1, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.User, 1, Ct));

        _films.VerifyNoOtherCalls();
        Assert.Equal(4, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal("access.denied user", e));
    }

    [Fact]
    public async Task Add_StoresTheParsedFilmWithAllCopiesOnTheShelf()
    {
        Film? stored = null;
        _films.Setup(f => f.AddAsync(Sessions.Admin.UserId, It.IsAny<Film>(), It.IsAny<CancellationToken>()))
            .Callback<int, Film, CancellationToken>((_, film, _) => stored = film)
            .ReturnsAsync(4);

        var id = await Service().AddAsync(Sessions.Admin, Valid with { Title = "  Sisli Liman  " }, Ct);

        Assert.Equal(4, id);
        Assert.NotNull(stored);
        Assert.Equal("Sisli Liman", stored.Title);
        Assert.Equal(2019, stored.Year);
        Assert.Equal(25.50m, stored.DailyPrice);
        Assert.Equal(3, stored.TotalCopies);
        Assert.Equal(3, stored.AvailableCopies);
        Assert.True(stored.IsListed);
        Assert.Equal(["film.changed admin"], _audit.Entries);
    }

    [Fact]
    public async Task Add_EmptyForm_ReportsEveryRequiredField()
    {
        var empty = new FilmInput(null, " ", "", null, "", null, "", 1, true);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, empty, Ct));

        Assert.Equal(
            [nameof(FilmInput.Title), nameof(FilmInput.Genre), nameof(FilmInput.Director), nameof(FilmInput.About), nameof(FilmInput.Year), nameof(FilmInput.DailyPrice)],
            error.FieldErrors.Keys);
        _films.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Year", "1899")]
    [InlineData("Year", "2027")]
    [InlineData("Year", "iki bin")]
    [InlineData("DailyPrice", "0")]
    [InlineData("DailyPrice", "-5")]
    [InlineData("DailyPrice", "12,345")]
    [InlineData("DailyPrice", "1.250")]
    [InlineData("DailyPrice", "100000000")]
    [InlineData("ImageUrl", "C:\\afis.png")]
    [InlineData("ImageUrl", "\\\\sunucu\\pay\\afis.png")]
    [InlineData("ImageUrl", "file:///C:/afis.png")]
    [InlineData("Copies", "0")]
    [InlineData("Copies", "1001")]
    [InlineData("Title", "51")]
    public async Task Add_InvalidValue_IsReportedForItsFieldOnly(string field, string value)
    {
        var input = field switch
        {
            "Year" => Valid with { Year = value },
            "DailyPrice" => Valid with { DailyPrice = value },
            "ImageUrl" => Valid with { ImageUrl = value },
            "Copies" => Valid with { Copies = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            _ => Valid with { Title = new string('a', int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)) },
        };

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, input, Ct));

        Assert.Equal([field], error.FieldErrors.Keys);
        _films.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_WithoutImage_IsAllowed()
    {
        _films.Setup(f => f.AddAsync(Sessions.Admin.UserId, It.Is<Film>(film => film.ImageUrl == null), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Service().AddAsync(Sessions.Admin, Valid with { ImageUrl = "  " }, Ct);
    }

    [Theory]
    [InlineData(FilmUpdateOutcome.CopiesBelowRented, FilmService.CopiesBelowRented)]
    [InlineData(FilmUpdateOutcome.NotFound, FilmService.NotFound)]
    public async Task Update_RefusedByTheRepository_BecomesARuleMessage(FilmUpdateOutcome outcome, string message)
    {
        _films.Setup(f => f.UpdateAsync(Sessions.Admin.UserId, It.Is<Film>(film => film.Id == 3), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 3, Valid, Ct));

        Assert.Equal(message, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Delete_FilmWithRentals_IsRefused()
    {
        _films.Setup(f => f.DeleteAsync(Sessions.Admin.UserId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(FilmDeleteOutcome.HasRentals);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteAsync(Sessions.Admin, 3, Ct));

        Assert.Equal(FilmService.HasRentals, error.Message);
    }

    [Fact]
    public async Task Get_UnlistedFilm_IsMissingForAUserButVisibleToAnAdmin()
    {
        var hidden = new Film { Id = 3, Title = "Gizli", IsListed = false };
        _films.Setup(f => f.FindAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(hidden);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().GetAsync(Sessions.User, 3, Ct));

        Assert.Equal(FilmService.NotFound, error.Message);
        Assert.Same(hidden, await Service().GetAsync(Sessions.Admin, 3, Ct));
    }

    private FilmService Service() => new(_films.Object, _time, _audit);
}
