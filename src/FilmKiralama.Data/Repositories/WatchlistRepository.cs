using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class WatchlistRepository(IDbContextFactory<FilmDbContext> contextFactory) : IWatchlistRepository
{
    public async Task<WatchlistAddOutcome> AddAsync(int userId, int filmId, DateTime now, int maxEntries, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Taken before the list is read: the size limit and the duplicate check hold for adds that
            // run at the same time.
            await Locks.WatchlistAsync(db, userId, cancellationToken).ConfigureAwait(false);
            await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);

            if (!await db.Films.AnyAsync(f => f.Id == filmId && f.IsListed, cancellationToken).ConfigureAwait(false))
            {
                return WatchlistAddOutcome.FilmNotFound;
            }

            var own = db.WatchlistEntries.Where(w => w.UserId == userId);
            if (await own.AnyAsync(w => w.FilmId == filmId, cancellationToken).ConfigureAwait(false))
            {
                return WatchlistAddOutcome.AlreadyListed;
            }

            if (await own.CountAsync(cancellationToken).ConfigureAwait(false) >= maxEntries)
            {
                return WatchlistAddOutcome.ListFull;
            }

            db.WatchlistEntries.Add(new WatchlistEntry { UserId = userId, FilmId = filmId, AddedAt = now });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return WatchlistAddOutcome.Added;
        }
        catch (DbUpdateException)
        {
            // The unique index refused a second entry of the same film (a writer that skipped the lock).
            await using var check = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            if (await check.WatchlistEntries.AnyAsync(w => w.UserId == userId && w.FilmId == filmId, CancellationToken.None).ConfigureAwait(false))
            {
                return WatchlistAddOutcome.AlreadyListed;
            }

            throw;
        }
    }

    public async Task<bool> RemoveAsync(int userId, int entryId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);

        // The owner is part of the condition: another account's entry is the same as none.
        var removed = await db.WatchlistEntries
            .Where(w => w.Id == entryId && w.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return removed == 1;
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, userId, admin: false, cancellationToken).ConfigureAwait(false);
        var rows = await (
                from entry in db.WatchlistEntries.AsNoTracking()
                join film in db.Films.AsNoTracking() on entry.FilmId equals film.Id
                where entry.UserId == userId
                orderby entry.AddedAt descending, entry.Id descending
                select new
                {
                    entry.Id,
                    entry.AddedAt,
                    Film = film,
                    Count = db.Reviews.Count(r => r.FilmId == film.Id && !r.IsHidden),
                    Sum = db.Reviews.Where(r => r.FilmId == film.Id && !r.IsHidden).Sum(r => (int?)r.Rating) ?? 0,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new WatchlistItem(r.Id, r.Film, new RatingTotals(r.Count, r.Sum), r.AddedAt))];
    }
}
