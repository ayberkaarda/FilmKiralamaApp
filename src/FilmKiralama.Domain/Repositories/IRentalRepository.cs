using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>Rentals and returns. Every write runs in one transaction under the film's lock.</summary>
public interface IRentalRepository
{
    /// <summary>
    /// Rents one copy: under the film's lock it checks that the film is listed, has a copy on the
    /// shelf, still costs <see cref="RentalRequest.ExpectedDailyPrice"/> a day and is not rented by the
    /// same account already, then stores the rental and takes the copy off the shelf.
    /// </summary>
    Task<RentResult> RentAsync(RentalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an active rental: marks it returned on <paramref name="returnedOn"/>, stores the late fee
    /// <paramref name="lateFee"/> computes from its due day and puts the copy back on the shelf. With
    /// <paramref name="ownerId"/> set, a rental of another account is treated as missing.
    /// </summary>
    Task<ReturnResult> ReturnAsync(
        int rentalId,
        int? ownerId,
        DateOnly returnedOn,
        Func<DateOnly?, decimal> lateFee,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Active rentals with film and account, ordered by id; only those of <paramref name="ownerId"/> when
    /// it is set, and only films matching <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<RentalDetails>> GetActiveAsync(int? ownerId, FilmFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>A rental to store; the price was computed from <see cref="ExpectedDailyPrice"/>.</summary>
public sealed record RentalRequest(
    int FilmId,
    int UserId,
    DateOnly RentedOn,
    DateOnly DueOn,
    decimal ExpectedDailyPrice,
    decimal Price);

public enum RentOutcome
{
    Rented = 0,
    FilmNotFound = 1,

    /// <summary>The film is unlisted or has no copy on the shelf.</summary>
    NotAvailable = 2,

    /// <summary>The account already has an active rental of the film.</summary>
    AlreadyRented = 3,

    /// <summary>The daily price changed after it was shown; nothing was written.</summary>
    PriceChanged = 4,
}

public readonly record struct RentResult(RentOutcome Outcome, int RentalId);

public enum ReturnOutcome
{
    Returned = 0,

    /// <summary>No active rental with that id (or not one of the owner's); nothing was written.</summary>
    NotFound = 1,
}

public readonly record struct ReturnResult(ReturnOutcome Outcome, DateOnly? DueOn, decimal LateFee);
