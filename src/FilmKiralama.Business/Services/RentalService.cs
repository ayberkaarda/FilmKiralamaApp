using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using FluentValidation;

namespace FilmKiralama.Business.Services;

/// <summary>What a rental will cost and when it ends, shown before it is confirmed.</summary>
public sealed record RentalQuote(int FilmId, int Days, decimal DailyPrice, decimal Total, DateOnly StartsOn, DateOnly DueOn);

/// <summary>A stored rental.</summary>
public sealed record RentalReceipt(int RentalId, decimal Total, DateOnly StartsOn, DateOnly DueOn);

/// <summary>What returning a rental today would cost in late fees.</summary>
public sealed record ReturnPreview(int OverdueDays, decimal LateFee);

/// <summary>A completed return.</summary>
public sealed record ReturnReceipt(int OverdueDays, decimal LateFee);

public interface IRentalService
{
    /// <summary>The current settings (late fee per day, longest rental).</summary>
    RentalPolicy Policy { get; }

    /// <summary>Price and dates of renting <paramref name="days"/> days from today, at the given daily price.</summary>
    RentalQuote Quote(int filmId, decimal dailyPrice, int days);

    /// <summary>
    /// Rents one copy of a film for the signed-in account. Refused when the film has no copy left, when the
    /// account already has an active rental of it, or when its price is no longer the quoted one.
    /// </summary>
    Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default);

    /// <summary>The signed-in account's own active rentals.</summary>
    Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Every active rental that matches <paramref name="filter"/> (admin only).</summary>
    Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default);

    /// <summary>The late fee a return today would cost.</summary>
    ReturnPreview PreviewReturn(RentalDetails rental);

    /// <summary>
    /// Returns an active rental and records its late fee. A user can return only their own rentals; a
    /// rental of another account gets the same answer as one that does not exist. Admins can return any.
    /// </summary>
    Task<ReturnReceipt> ReturnAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default);
}

internal sealed class RentalService(
    IRentalRepository rentals,
    TimeProvider timeProvider,
    RentalPolicy policy,
    IAuditLog audit,
    IValidator<RentalQuoteInput> quoteValidator) : IRentalService
{
    internal const string FilmNotFound = "Film bulunamadı.";
    internal const string NotAvailable = "Bu film şu anda kiralanamıyor; stokta kopyası kalmadı.";
    internal const string AlreadyRented = "Bu filmi zaten kiraladınız. Aynı filmi iade etmeden tekrar kiralayamazsınız.";
    internal const string PriceChanged = "Filmin günlük ücreti değişti. Lütfen güncel ücreti kontrol edip yeniden deneyin.";
    internal const string RentalNotFound = "Kiralama bulunamadı veya zaten iade edilmiş.";
    internal const string TotalTooHigh = "Toplam tutar çok yüksek; lütfen daha kısa bir süre seçin.";

    public RentalPolicy Policy => policy;

    public RentalQuote Quote(int filmId, decimal dailyPrice, int days)
    {
        quoteValidator.ValidateOrThrow(new RentalQuoteInput(days, dailyPrice));

        var total = LateFeeCalculator.RentalPrice(days, dailyPrice);
        var today = timeProvider.Today();
        return new RentalQuote(filmId, days, dailyPrice, total, today, today.AddDays(days));
    }

    public async Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        ArgumentNullException.ThrowIfNull(quote);

        // The dates and the total are worked out again here; only the film, the days and the price the
        // customer saw come from the screen, and the repository refuses a price that has changed since.
        var current = Quote(quote.FilmId, quote.DailyPrice, quote.Days);
        var request = new RentalRequest(current.FilmId, session.UserId, current.StartsOn, current.DueOn, current.DailyPrice, current.Total);
        var result = await audit.AsActorAsync(session, () => rentals.RentAsync(request, cancellationToken)).ConfigureAwait(false);

        var role = AuditCodes.Of(session.Role);
        switch (result.Outcome)
        {
            case RentOutcome.Rented:
                audit.Record(AuditEvent.RentalCreated, role);
                return new RentalReceipt(result.RentalId, current.Total, current.StartsOn, current.DueOn);
            case RentOutcome.NotAvailable:
                audit.Record(AuditEvent.RentalOutOfStock, role);
                throw new BusinessRuleException(NotAvailable);
            case RentOutcome.AlreadyRented:
                throw new BusinessRuleException(AlreadyRented);
            case RentOutcome.PriceChanged:
                throw new BusinessRuleException(PriceChanged);
            default:
                throw new BusinessRuleException(FilmNotFound);
        }
    }

    public Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return audit.AsActorAsync(session, () => rentals.GetActiveAsync(new RentalActor(session.UserId, AsAdmin: false), FilmFilter.None, cancellationToken));
    }

    public Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        ArgumentNullException.ThrowIfNull(filter);
        return audit.AsActorAsync(session, () => rentals.GetActiveAsync(new RentalActor(session.UserId, AsAdmin: true), filter, cancellationToken));
    }

    public ReturnPreview PreviewReturn(RentalDetails rental)
    {
        ArgumentNullException.ThrowIfNull(rental);

        var today = timeProvider.Today();
        var days = LateFeeCalculator.OverdueDays(rental.DueOn, today);
        return new ReturnPreview(days, LateFeeCalculator.LateFee(days, policy.DailyLateFee));
    }

    public async Task<ReturnReceipt> ReturnAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);

        var today = timeProvider.Today();
        var dailyLateFee = policy.DailyLateFee;
        var result = await audit.AsActorAsync(
            session,
            () => rentals.ReturnAsync(
                rentalId,
                Access.RentalActorOf(session),
                today,
                dueOn => LateFeeCalculator.LateFee(dueOn, today, dailyLateFee),
                cancellationToken)).ConfigureAwait(false);
        if (result.Outcome != ReturnOutcome.Returned)
        {
            throw new BusinessRuleException(RentalNotFound);
        }

        audit.Record(AuditEvent.RentalReturned, AuditCodes.Of(session.Role));
        return new ReturnReceipt(LateFeeCalculator.OverdueDays(result.DueOn, today), result.LateFee);
    }
}
