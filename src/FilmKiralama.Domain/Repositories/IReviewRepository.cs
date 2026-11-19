using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>Ratings and comments of films. Customers reach only their own reviews; admins can hide any.</summary>
public interface IReviewRepository
{
    /// <summary>
    /// Stores or changes the review of account <paramref name="userId"/> for <paramref name="filmId"/>,
    /// only when that account has returned a rental of the film. A review hidden by an admin stays
    /// hidden. Two saves of the same account and film at once leave one review. The account is checked
    /// in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<ReviewSaveOutcome> SaveAsync(int userId, int filmId, int rating, string? comment, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The account's own review of a film, hidden or not, or <see langword="null"/>.</summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<ReviewDetails?> FindOwnAsync(int userId, int filmId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes review <paramref name="reviewId"/> when it belongs to <paramref name="userId"/>; another
    /// account's review is treated as missing. A review an admin hid is not removed but marked deleted
    /// (see <see cref="Review.IsDeleted"/>), so writing it again leaves it hidden.
    /// </summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<bool> DeleteOwnAsync(int userId, int reviewId, CancellationToken cancellationToken = default);

    /// <summary>The visible reviews of a film, newest first, at most <paramref name="max"/>, and the totals of all visible ones.</summary>
    Task<(IReadOnlyList<ReviewDetails> Reviews, RatingTotals Totals)> GetVisibleAsync(int filmId, int max, CancellationToken cancellationToken = default);

    /// <summary>The films the account has returned at least once, by title, with its own review.</summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Every review (or only the hidden ones), newest first, one page, for the admin <paramref name="adminId"/>.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<PagedResult<ReviewDetails>> GetAllAsync(int adminId, bool hiddenOnly, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Hides or shows a review again, for the admin <paramref name="adminId"/>; <see langword="false"/> when it does not exist.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<bool> SetHiddenAsync(int adminId, int reviewId, bool hidden, CancellationToken cancellationToken = default);
}

public enum ReviewSaveOutcome
{
    Created = 0,
    Updated = 1,

    /// <summary>The account has not returned a rental of the film (or the film does not exist); nothing was written.</summary>
    NotEligible = 2,
}
