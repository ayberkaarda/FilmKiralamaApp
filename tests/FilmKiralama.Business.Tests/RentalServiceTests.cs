using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

public sealed class RentalServiceTests
{
    /// <summary>Monday 2 March 2026 on the local clock (the fake clock's zone is UTC).</summary>
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IRentalRepository> _rentals = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();
    private RentalPolicy _policy = new() { DailyLateFee = 7.5m, MaxRentalDays = 30 };
    private IClock? _clock;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Quote_IsTheDailyPriceTimesTheDaysFromToday()
    {
        var quote = Service().Quote(3, 12.5m, 4);

        Assert.Equal(50m, quote.Total);
        Assert.Equal(Today, quote.StartsOn);
        Assert.Equal(Today.AddDays(4), quote.DueOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void Quote_DaysOutsideTheSettings_AreRefused(int days)
    {
        var error = Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 12.5m, days));

        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", error.Message);
    }

    [Fact]
    public void Quote_TotalBeyondTheMoneyColumn_IsRefused()
    {
        _policy = new RentalPolicy { MaxRentalDays = 3650 };

        Assert.Throws<BusinessRuleException>(() => Service().Quote(3, 99_999_999m, 2));
    }

    [Fact]
    public async Task Rent_SendsTheAccountOfTheSessionAndRecomputedValues()
    {
        RentalRequest? sent = null;
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RentalRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new RentResult(RentOutcome.Rented, 11));

        // The screen's total and dates are not trusted: only film, days and price are taken from it.
        var forged = new RentalQuote(3, 2, 10m, 0.01m, Today.AddDays(-30), Today.AddDays(-28));
        var receipt = await Service().RentAsync(Sessions.User, forged, Ct);

        Assert.Equal(new RentalRequest(3, Sessions.User.UserId, Today, Today.AddDays(2), 10m, 20m), sent);
        Assert.Equal(11, receipt.RentalId);
        Assert.Equal(20m, receipt.Total);
        Assert.Equal(["rental.created user"], _audit.Entries);
    }

    [Theory]
    [InlineData(RentOutcome.NotAvailable, RentalService.NotAvailable)]
    [InlineData(RentOutcome.AlreadyRented, RentalService.AlreadyRented)]
    [InlineData(RentOutcome.PriceChanged, RentalService.PriceChanged)]
    [InlineData(RentOutcome.FilmNotFound, RentalService.FilmNotFound)]
    public async Task Rent_RefusedByTheRepository_BecomesARuleMessage(RentOutcome outcome, string message)
    {
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RentResult(outcome, 0));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RentAsync(Sessions.User, Service().Quote(3, 10m, 1), Ct));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task Return_ByAUser_IsLimitedToTheirOwnRentals()
    {
        _rentals.Setup(r => r.ReturnAsync(9, new RentalActor(Sessions.User.UserId, AsAdmin: false), Today, It.IsAny<Func<DateOnly?, decimal>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnResult(ReturnOutcome.NotFound, null, 0m));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().ReturnAsync(Sessions.User, 9, Ct));

        // Another account's rental and a missing one get the same answer.
        Assert.Equal(RentalService.RentalNotFound, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Return_ByAnAdmin_HasNoOwnerFilter()
    {
        _rentals.Setup(r => r.ReturnAsync(9, new RentalActor(Sessions.Admin.UserId, AsAdmin: true), Today, It.IsAny<Func<DateOnly?, decimal>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnResult(ReturnOutcome.Returned, Today, 0m));

        var receipt = await Service().ReturnAsync(Sessions.Admin, 9, Ct);

        Assert.Equal(0m, receipt.LateFee);
        Assert.Equal(["rental.returned admin"], _audit.Entries);
    }

    [Fact]
    public async Task Return_ByAnAccountThatIsGone_AsksToSignInAgain()
    {
        _rentals.Setup(r => r.ReturnAsync(9, new RentalActor(Sessions.Admin.UserId, AsAdmin: true), Today, It.IsAny<Func<DateOnly?, decimal>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ActorRevokedException());
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());

        var returned = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().ReturnAsync(Sessions.Admin, 9, Ct));
        var rented = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().RentAsync(Sessions.User, Service().Quote(3, 10m, 1), Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, returned.Message);
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, rented.Message);
        Assert.Equal(["access.session_revoked admin", "access.session_revoked user"], _audit.Entries);
    }

    [Fact]
    public async Task Return_ComputesTheLateFeeFromTheDueDayReadUnderTheLock()
    {
        Func<DateOnly?, decimal>? feeOf = null;
        _rentals.Setup(r => r.ReturnAsync(9, new RentalActor(Sessions.User.UserId, AsAdmin: false), Today, It.IsAny<Func<DateOnly?, decimal>>(), It.IsAny<CancellationToken>()))
            .Callback<int, RentalActor, DateOnly, Func<DateOnly?, decimal>, CancellationToken>((_, _, _, fee, _) => feeOf = fee)
            .ReturnsAsync(new ReturnResult(ReturnOutcome.Returned, Today.AddDays(-4), 30m));

        var receipt = await Service().ReturnAsync(Sessions.User, 9, Ct);

        Assert.NotNull(feeOf);
        Assert.Equal(30m, feeOf(Today.AddDays(-4)));
        Assert.Equal(0m, feeOf(Today));
        Assert.Equal(0m, feeOf(null));
        Assert.Equal(4, receipt.OverdueDays);
        Assert.Equal(30m, receipt.LateFee);
    }

    /// <summary>
    /// The computer's clock is three days behind the database's: the rental starts, and the return is
    /// charged, on the database's day, so a wrong clock neither backdates a rental nor waives a late fee.
    /// </summary>
    [Fact]
    public async Task RentAndReturn_UseTheDatabaseClockNotTheComputers()
    {
        var databaseDay = Today.AddDays(3);
        _clock = new TimeProviderClock(new FakeTimeProvider(new DateTimeOffset(2026, 3, 5, 10, 0, 0, TimeSpan.Zero)));
        RentalRequest? sent = null;
        _rentals.Setup(r => r.RentAsync(It.IsAny<RentalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RentalRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new RentResult(RentOutcome.Rented, 11));
        _rentals.Setup(r => r.ReturnAsync(9, new RentalActor(Sessions.User.UserId, AsAdmin: false), databaseDay, It.IsAny<Func<DateOnly?, decimal>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnResult(ReturnOutcome.Returned, Today, 22.5m));

        var receipt = await Service().RentAsync(Sessions.User, Service().Quote(3, 10m, 2), Ct);
        var returned = await Service().ReturnAsync(Sessions.User, 9, Ct);

        Assert.Equal(databaseDay, sent!.RentedOn);
        Assert.Equal(databaseDay.AddDays(2), receipt.DueOn);
        Assert.Equal(3, returned.OverdueDays);
    }

    [Fact]
    public void PreviewReturn_ShowsTodaysLateFee()
    {
        var rental = new RentalDetails(9, 3, "Sisli Liman", "Gerilim", 2019, "Ada Örnek", null, 2, "ornek", Today.AddDays(-10), Today.AddDays(-2), 20m, RentalStatus.Active);

        Assert.Equal(new ReturnPreview(2, 15m), Service().PreviewReturn(rental));
    }

    [Fact]
    public async Task ActiveRentals_OfEveryone_AreForAdminsOnly()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetActiveRentalsAsync(Sessions.User, FilmFilter.None, Ct));
        _rentals.VerifyNoOtherCalls();

        _rentals.Setup(r => r.GetActiveAsync(new RentalActor(Sessions.User.UserId, AsAdmin: false), FilmFilter.None, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await Service().GetMyActiveRentalsAsync(Sessions.User, Ct);
        _rentals.Verify(r => r.GetActiveAsync(new RentalActor(Sessions.User.UserId, AsAdmin: false), FilmFilter.None, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1, 30, 1)]
    [InlineData(10_001, 30, 1)]
    [InlineData(10.555, 30, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 3651, 1)]
    [InlineData(10, 30, 0)]
    public void Policy_Validate_ReportsInvalidSettings(double fee, int maxDays, int expectedProblems)
    {
        var policy = new RentalPolicy { DailyLateFee = (decimal)fee, MaxRentalDays = maxDays };

        Assert.Equal(expectedProblems, policy.Validate().Count);
    }

    private RentalService Service() => new(_rentals.Object, _time, _clock ?? new TimeProviderClock(_time), _policy, _audit, TestValidators.Quote(_policy));
}
