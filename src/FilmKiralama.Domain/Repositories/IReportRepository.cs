using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>
/// The admin's reports and the customer's recommendations. The reports check the admin's stored role,
/// the recommendations the stored account, before anything is read.
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// The <paramref name="top"/> films rented most often with a start day from <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/> inclusive, most rented first, then by title and id.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<IReadOnlyList<RentalCountRow>> GetMostRentedAsync(Actor actor, DateOnly firstDay, DateOnly lastDay, int top, CancellationToken cancellationToken = default);

    /// <summary>Active rentals due before <paramref name="today"/>, the longest overdue first, then by id.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<IReadOnlyList<OverdueRentalRow>> GetOverdueAsync(Actor actor, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Rentals returned from <paramref name="firstDay"/> to <paramref name="lastDay"/> inclusive with a late fee, by return day and id.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<IReadOnlyList<LateFeeRow>> GetLateFeesAsync(Actor actor, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);

    /// <summary>The genres of every rental the account has made, active or returned, with their numbers.</summary>
    /// <exception cref="ActorRevokedException">The account no longer exists or its session was revoked.</exception>
    Task<IReadOnlyList<GenreCount>> GetRentedGenresAsync(Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Listed films of <paramref name="genres"/> with a copy on the shelf that the account has never rented,
    /// with the totals of their visible ratings. The account is checked first.
    /// </summary>
    /// <exception cref="ActorRevokedException">The account no longer exists or its session was revoked.</exception>
    Task<IReadOnlyList<CatalogEntry>> GetUnrentedAvailableAsync(Actor actor, IReadOnlyCollection<string> genres, CancellationToken cancellationToken = default);
}
