using System.Globalization;
using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using FluentValidation;

namespace FilmKiralama.Business.Reviews;

/// <summary>A rating of one to five stars and an optional short comment, as the review form enters them.</summary>
public sealed record ReviewInput(int Rating, string? Comment);

/// <summary>A visible review as every customer sees it: the author's name is masked.</summary>
public sealed record PublicReview(string Author, int Rating, string? Comment, DateOnly Date);

/// <summary>A film's average of its visible ratings (one decimal), their number and the newest visible reviews.</summary>
public sealed record FilmReviews(decimal? Average, int Count, IReadOnlyList<PublicReview> Reviews);

/// <summary>The signed-in account's own review of a film.</summary>
public sealed record OwnReview(int Id, int FilmId, int Rating, string? Comment, bool IsHidden);

/// <summary>Limits of reviews.</summary>
public static class ReviewPolicy
{
    public const int MinRating = 1;
    public const int MaxRating = 5;

    /// <summary>Longest comment, in characters (the column holds 500).</summary>
    public const int MaxCommentLength = 500;

    /// <summary>Newest visible reviews shown with a film.</summary>
    public const int ShownWithFilm = 50;

    /// <summary>Reviews per page of the admin's review list.</summary>
    public const int AdminPageSize = 50;
}

/// <summary>
/// Ratings and comments. A customer rates only films they rented and returned, once per film, and can
/// change or delete their own review; an admin can hide a review. Nothing about a review (its text, its
/// author, its film) is written to the log, only fixed codes.
/// </summary>
public interface IReviewService
{
    /// <summary>Stores or changes the account's review of a film it has returned.</summary>
    /// <exception cref="BusinessRuleException">Invalid rating or comment, or the film was never returned by the account.</exception>
    Task SaveAsync(UserSession session, int filmId, ReviewInput input, CancellationToken cancellationToken = default);

    /// <summary>The account's own review of a film, or <see langword="null"/>.</summary>
    Task<OwnReview?> GetOwnAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);

    /// <summary>Deletes one of the account's own reviews; another account's review gets the answer of a missing one.</summary>
    Task DeleteOwnAsync(UserSession session, int reviewId, CancellationToken cancellationToken = default);

    /// <summary>The visible reviews and the average rating of a film the session may see.</summary>
    Task<FilmReviews> GetFilmReviewsAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);

    /// <summary>The films the account has returned and may therefore rate.</summary>
    Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Every review with its full author name, newest first, one page (admin only).</summary>
    Task<PagedResult<ReviewDetails>> GetAllAsync(UserSession session, bool hiddenOnly, int page, CancellationToken cancellationToken = default);

    /// <summary>Hides a review from customers, or shows it again (admin only).</summary>
    Task SetHiddenAsync(UserSession session, int reviewId, bool hidden, CancellationToken cancellationToken = default);
}

internal sealed class ReviewService(
    IReviewRepository reviews,
    IFilmRepository films,
    IClock clock,
    TimeProvider timeProvider,
    IAuditLog audit,
    IValidator<ReviewFields> validator) : IReviewService
{
    internal const string NotEligible = "Bir filmi puanlamak için önce kiralayıp iade etmiş olmanız gerekir.";
    internal const string ReviewNotFound = "Değerlendirme bulunamadı.";
    internal const string FilmNotFound = "Film bulunamadı.";

    public async Task SaveAsync(UserSession session, int filmId, ReviewInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        ArgumentNullException.ThrowIfNull(input);

        var fields = ReviewFields.Create(input);
        validator.ValidateOrThrow(fields);
        var comment = fields.NormalizedComment;
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await audit.AsActorAsync(
            session,
            () => reviews.SaveAsync(session.Actor, filmId, fields.Rating, comment, now, cancellationToken)).ConfigureAwait(false);
        if (outcome == ReviewSaveOutcome.NotEligible)
        {
            throw new BusinessRuleException(NotEligible);
        }

        // A new review and a change share one code.
        audit.Record(AuditEvent.ReviewSaved, AuditCodes.Of(session.Role));
    }

    public async Task<OwnReview?> GetOwnAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var review = await audit.AsActorAsync(session, () => reviews.FindOwnAsync(session.Actor, filmId, cancellationToken)).ConfigureAwait(false);
        return review is null ? null : new OwnReview(review.Id, review.FilmId, review.Rating, review.Comment, review.IsHidden);
    }

    public async Task DeleteOwnAsync(UserSession session, int reviewId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var deleted = await audit.AsActorAsync(session, () => reviews.DeleteOwnAsync(session.Actor, reviewId, cancellationToken)).ConfigureAwait(false);
        if (!deleted)
        {
            throw new BusinessRuleException(ReviewNotFound);
        }

        audit.Record(AuditEvent.ReviewDeleted, AuditCodes.Of(session.Role));
    }

    public async Task<FilmReviews> GetFilmReviewsAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);

        // The reviews of a film a customer may not see are as missing as the film.
        var film = await films.FindAsync(filmId, cancellationToken).ConfigureAwait(false);
        if (film is null || (!film.IsListed && !session.IsAdmin))
        {
            throw new BusinessRuleException(FilmNotFound);
        }

        var (visible, totals) = await audit.AsActorAsync(
            session,
            () => reviews.GetVisibleAsync(session.Actor, filmId, ReviewPolicy.ShownWithFilm, cancellationToken)).ConfigureAwait(false);
        var zone = timeProvider.LocalTimeZone;
        return new FilmReviews(
            RatingMath.Average(totals),
            totals.Count,
            visible
                .Select(r => new PublicReview(
                    MaskAuthor(r.UserName),
                    r.Rating,
                    r.Comment,
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc), zone))))
                .ToList());
    }

    public Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return audit.AsActorAsync(session, () => reviews.GetReviewableAsync(session.Actor, cancellationToken));
    }

    public Task<PagedResult<ReviewDetails>> GetAllAsync(UserSession session, bool hiddenOnly, int page, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        return audit.AsActorAsync(session, () => reviews.GetAllAsync(session.Actor, hiddenOnly, Math.Max(1, page), ReviewPolicy.AdminPageSize, cancellationToken));
    }

    public async Task SetHiddenAsync(UserSession session, int reviewId, bool hidden, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var changed = await audit.AsActorAsync(session, () => reviews.SetHiddenAsync(session.Actor, reviewId, hidden, cancellationToken)).ConfigureAwait(false);
        if (!changed)
        {
            throw new BusinessRuleException(ReviewNotFound);
        }

        audit.Record(AuditEvent.ReviewModerated, AuditRole.Admin);
    }

    /// <summary>
    /// The author as other customers see it: the first letter and three stars. A user name is a sign-in
    /// name, so it is never shown in full to other customers.
    /// </summary>
    internal static string MaskAuthor(string userName)
    {
        if (string.IsNullOrEmpty(userName))
        {
            return "***";
        }

        var first = StringInfo.GetNextTextElement(userName);
        return first.ToUpper(InputRules.Turkish) + "***";
    }
}

/// <summary>Average ratings, worked out from the totals the database adds up.</summary>
public static class RatingMath
{
    /// <summary>Decimals of an average rating.</summary>
    public const int Decimals = 1;

    /// <summary>
    /// The average of <paramref name="totals"/> in decimal arithmetic, rounded to one decimal with
    /// halves away from zero (3.25 is 3.3, 3.666... is 3.7); <see langword="null"/> without ratings.
    /// </summary>
    public static decimal? Average(RatingTotals totals) =>
        totals.Count <= 0 ? null : decimal.Round((decimal)totals.Sum / totals.Count, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Whether the shown (rounded) average is at least <paramref name="minimum"/> stars. The catalog
    /// filter computes the same in the database as 20 x sum >= (20 x minimum - 1) x count.
    /// </summary>
    public static bool MeetsMinimum(RatingTotals totals, int minimum) => Average(totals) is { } average && average >= minimum;

    /// <summary>The average as the screens show it, for example "4,3".</summary>
    public static string Format(decimal? average) =>
        average is { } value ? value.ToString("0.0", InputRules.Turkish) : "-";
}
