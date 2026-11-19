using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class ReviewRepository(IDbContextFactory<FilmDbContext> contextFactory) : IReviewRepository
{
    public async Task<ReviewSaveOutcome> SaveAsync(int userId, int filmId, int rating, string? comment, DateTime now, CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await SaveOnceAsync(userId, filmId, rating, comment, now, insert: true, cancellationToken).ConfigureAwait(false);
            return outcome ?? throw new InvalidOperationException("A save that may insert always has an outcome.");
        }
        catch (DbUpdateException)
        {
            // The unique index took a review of the same account and film stored concurrently; this save
            // becomes a change of it, in a transaction of its own with the account and the eligibility
            // checked again. When no such review exists the insert failed for another reason, which is
            // thrown as it was.
            var retried = await SaveOnceAsync(userId, filmId, rating, comment, now, insert: false, CancellationToken.None).ConfigureAwait(false);
            if (retried is { } changed)
            {
                return changed;
            }

            throw;
        }
    }

    /// <summary>
    /// One attempt of <see cref="SaveAsync"/> in its own transaction. Without <paramref name="insert"/> it
    /// only changes an existing review and returns <see langword="null"/> when there is none.
    /// </summary>
    private async Task<ReviewSaveOutcome?> SaveOnceAsync(int userId, int filmId, int rating, string? comment, DateTime now, bool insert, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);

        // Only someone who has watched the film may rate it: a returned rental of this account.
        var eligible = await db.Rentals
            .AnyAsync(r => r.UserId == userId && r.FilmId == filmId && r.Status == RentalStatus.Returned, cancellationToken)
            .ConfigureAwait(false);
        if (!eligible)
        {
            return ReviewSaveOutcome.NotEligible;
        }

        var updated = await UpdateAsync(db, userId, filmId, rating, comment, now, cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            if (!insert)
            {
                return null;
            }

            db.Reviews.Add(new Review { UserId = userId, FilmId = filmId, Rating = rating, Comment = comment, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated ? ReviewSaveOutcome.Updated : ReviewSaveOutcome.Created;
    }

    public async Task<ReviewDetails?> FindOwnAsync(int userId, int filmId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);
        return await Rows(db)
            .Where(r => r.Review.UserId == userId && r.Review.FilmId == filmId && !r.Review.IsDeleted)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteOwnAsync(int userId, int reviewId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);

        // The owner is part of the condition: another account's review is the same as none. The review is
        // first marked deleted with its comment cleared, which also locks the row against a concurrent
        // hide or show until the commit; then it is removed only if no admin hid it. A hidden review
        // stays as a deleted row, so the author's next review of the film is hidden as well.
        var marked = await db.Reviews
            .Where(r => r.Id == reviewId && r.UserId == userId && !r.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted, true).SetProperty(r => r.Comment, (string?)null), cancellationToken)
            .ConfigureAwait(false);
        await db.Reviews
            .Where(r => r.Id == reviewId && r.UserId == userId && !r.IsHidden)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return marked == 1;
    }

    public async Task<(IReadOnlyList<ReviewDetails> Reviews, RatingTotals Totals)> GetVisibleAsync(int filmId, int max, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 0);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var visible = db.Reviews.AsNoTracking().Where(r => r.FilmId == filmId && !r.IsHidden);
        var count = await visible.CountAsync(cancellationToken).ConfigureAwait(false);
        var sum = count == 0 ? 0 : await visible.SumAsync(r => r.Rating, cancellationToken).ConfigureAwait(false);
        var reviews = await Rows(db)
            .Where(r => r.Review.FilmId == filmId && !r.Review.IsHidden)
            .OrderByDescending(r => r.Review.UpdatedAt)
            .ThenByDescending(r => r.Review.Id)
            .Take(max)
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return (reviews, new RatingTotals(count, sum));
    }

    public async Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);
        var query =
            from film in db.Films.AsNoTracking()
            where db.Rentals.Any(r => r.FilmId == film.Id && r.UserId == userId && r.Status == RentalStatus.Returned)
            let own = db.Reviews.FirstOrDefault(r => r.FilmId == film.Id && r.UserId == userId && !r.IsDeleted)
            orderby film.Title, film.Id
            select new ReviewableFilm(film.Id, film.Title, film.Genre, film.Year, own == null ? null : own.Id, own == null ? null : own.Rating);
        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<ReviewDetails>> GetAllAsync(int adminId, bool hiddenOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);
        var reviews = Rows(db).Where(r => !r.Review.IsDeleted && (!hiddenOnly || r.Review.IsHidden));
        var total = await reviews.CountAsync(cancellationToken).ConfigureAwait(false);
        var current = Paging.ClampPage(page, total, pageSize);
        var items = await reviews
            .OrderByDescending(r => r.Review.UpdatedAt)
            .ThenByDescending(r => r.Review.Id)
            .Skip((current - 1) * pageSize)
            .Take(pageSize)
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<ReviewDetails>(items, total, current, pageSize);
    }

    public async Task<bool> SetHiddenAsync(int adminId, int reviewId, bool hidden, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);
        // A review its author deleted is missing here too, so it can never be shown again.
        var changed = await db.Reviews
            .Where(r => r.Id == reviewId && !r.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsHidden, hidden), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed == 1;
    }

    /// <summary>
    /// Changes the account's review of the film, or takes back the one it deleted while hidden, keeping
    /// whether it is hidden; <see langword="false"/> when there is none.
    /// </summary>
    private static async Task<bool> UpdateAsync(FilmDbContext db, int userId, int filmId, int rating, string? comment, DateTime now, CancellationToken cancellationToken) =>
        await db.Reviews
            .Where(r => r.UserId == userId && r.FilmId == filmId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Rating, rating)
                    .SetProperty(r => r.Comment, comment)
                    .SetProperty(r => r.IsDeleted, false)
                    .SetProperty(r => r.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false) == 1;

    /// <summary>Reviews with their film title and author, still open to filters and ordering.</summary>
    private static IQueryable<ReviewRow> Rows(FilmDbContext db) =>
        from review in db.Reviews.AsNoTracking()
        join film in db.Films.AsNoTracking() on review.FilmId equals film.Id
        join user in db.Users.AsNoTracking() on review.UserId equals user.Id
        select new ReviewRow { Review = review, FilmTitle = film.Title, UserName = user.UserName };

    private static readonly System.Linq.Expressions.Expression<Func<ReviewRow, ReviewDetails>> Projection = r =>
        new ReviewDetails(r.Review.Id, r.Review.FilmId, r.FilmTitle, r.Review.UserId, r.UserName, r.Review.Rating, r.Review.Comment, r.Review.IsHidden, r.Review.UpdatedAt);

    private sealed class ReviewRow
    {
        public Review Review { get; init; } = null!;

        public string FilmTitle { get; init; } = string.Empty;

        public string UserName { get; init; } = string.Empty;
    }
}
