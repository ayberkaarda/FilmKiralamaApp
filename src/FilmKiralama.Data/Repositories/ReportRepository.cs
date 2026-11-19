using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class ReportRepository(IDbContextFactory<FilmDbContext> contextFactory) : IReportRepository
{
    public async Task<IReadOnlyList<RentalCountRow>> GetMostRentedAsync(Actor actor, DateOnly firstDay, DateOnly lastDay, int top, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);
        var counts =
            from rental in db.Rentals.AsNoTracking()
            where rental.RentedOn >= firstDay && rental.RentedOn <= lastDay
            group rental by rental.FilmId into g
            select new { FilmId = g.Key, Rentals = g.Count() };
        var rows = await (
                from count in counts
                join film in db.Films.AsNoTracking() on count.FilmId equals film.Id
                orderby count.Rentals descending, film.Title, film.Id
                select new { film.Id, film.Title, film.Genre, count.Rentals })
            .Take(top)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new RentalCountRow(r.Id, r.Title, r.Genre, r.Rentals))];
    }

    public async Task<IReadOnlyList<OverdueRentalRow>> GetOverdueAsync(Actor actor, DateOnly today, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);
        var rows = await (
                from rental in db.Rentals.AsNoTracking()
                join film in db.Films.AsNoTracking() on rental.FilmId equals film.Id
                join user in db.Users.AsNoTracking() on rental.UserId equals user.Id
                where rental.Status == RentalStatus.Active && rental.DueOn != null && rental.DueOn < today
                orderby rental.DueOn, rental.Id
                select new { rental.Id, film.Title, user.UserName, rental.RentedOn, rental.DueOn })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new OverdueRentalRow(r.Id, r.Title, r.UserName, r.RentedOn, r.DueOn!.Value))];
    }

    public async Task<IReadOnlyList<LateFeeRow>> GetLateFeesAsync(Actor actor, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);
        var rows = await (
                from rental in db.Rentals.AsNoTracking()
                join film in db.Films.AsNoTracking() on rental.FilmId equals film.Id
                join user in db.Users.AsNoTracking() on rental.UserId equals user.Id
                where rental.Status == RentalStatus.Returned
                    && rental.ReturnedOn >= firstDay && rental.ReturnedOn <= lastDay
                    && rental.LateFee != null && rental.DueOn != null
                orderby rental.ReturnedOn, rental.Id
                select new { rental.Id, film.Title, user.UserName, rental.DueOn, rental.ReturnedOn, rental.LateFee })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Money is summed by the business layer, in decimal; SQLite cannot add up decimals.
        return [.. rows.Where(r => r.LateFee > 0m).Select(r => new LateFeeRow(r.Id, r.Title, r.UserName, r.DueOn!.Value, r.ReturnedOn!.Value, r.LateFee!.Value))];
    }

    public async Task<IReadOnlyList<GenreCount>> GetRentedGenresAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        var userId = actor.UserId;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: false, cancellationToken).ConfigureAwait(false);
        var rows = await (
                from rental in db.Rentals.AsNoTracking()
                join film in db.Films.AsNoTracking() on rental.FilmId equals film.Id
                where rental.UserId == userId
                group rental by film.Genre into g
                select new { Genre = g.Key, Rentals = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new GenreCount(r.Genre, r.Rentals))];
    }

    public async Task<IReadOnlyList<CatalogEntry>> GetUnrentedAvailableAsync(Actor actor, IReadOnlyCollection<string> genres, CancellationToken cancellationToken = default)
    {
        var userId = actor.UserId;
        ArgumentNullException.ThrowIfNull(genres);
        if (genres.Count == 0)
        {
            return [];
        }

        var wanted = genres.ToList();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: false, cancellationToken).ConfigureAwait(false);
        var rows = await db.Films
            .AsNoTracking()
            .Where(f => f.IsListed && f.AvailableCopies > 0 && wanted.Contains(f.Genre))
            .Where(f => !db.Rentals.Any(r => r.FilmId == f.Id && r.UserId == userId))
            .Select(f => new
            {
                Film = f,
                Count = db.Reviews.Count(r => r.FilmId == f.Id && !r.IsHidden),
                Sum = db.Reviews.Where(r => r.FilmId == f.Id && !r.IsHidden).Sum(r => (int?)r.Rating) ?? 0,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new CatalogEntry(r.Film, new RatingTotals(r.Count, r.Sum)))];
    }
}
