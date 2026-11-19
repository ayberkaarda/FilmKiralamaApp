using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class FilmRepository(IDbContextFactory<FilmDbContext> contextFactory) : IFilmRepository
{
    public async Task<IReadOnlyList<Film>> GetCatalogAsync(FilmFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Matching(db.Films.AsNoTracking(), filter)
            .Where(f => f.IsListed && f.AvailableCopies > 0)
            .OrderBy(f => f.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FilmFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var films = db.Films.AsNoTracking();
        var genres = await films.Select(f => f.Genre).Distinct().OrderBy(g => g).ToListAsync(cancellationToken).ConfigureAwait(false);
        var directors = await films.Select(f => f.Director).Distinct().OrderBy(d => d).ToListAsync(cancellationToken).ConfigureAwait(false);
        var years = await films.Select(f => f.Year).Distinct().OrderByDescending(y => y).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new FilmFilterOptions(genres, directors, years);
    }

    public async Task<IReadOnlyList<Film>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Films.AsNoTracking().OrderBy(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Film?> FindAsync(int filmId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Films.AsNoTracking().SingleOrDefaultAsync(f => f.Id == filmId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(Film film, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(film);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = Copy(film);
        entity.AvailableCopies = film.TotalCopies;
        db.Films.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<FilmUpdateOutcome> UpdateAsync(Film film, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(film);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Locks.FilmAsync(db, film.Id, cancellationToken).ConfigureAwait(false);

        var stored = await db.Films.SingleOrDefaultAsync(f => f.Id == film.Id, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return FilmUpdateOutcome.NotFound;
        }

        // Counted under the lock: no rental or return of this film can run until the commit.
        var rented = await db.Rentals
            .CountAsync(r => r.FilmId == film.Id && r.Status == RentalStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        if (film.TotalCopies < rented)
        {
            return FilmUpdateOutcome.CopiesBelowRented;
        }

        stored.Title = film.Title;
        stored.Genre = film.Genre;
        stored.Year = film.Year;
        stored.Director = film.Director;
        stored.DailyPrice = film.DailyPrice;
        stored.ImageUrl = film.ImageUrl;
        stored.About = film.About;
        stored.IsListed = film.IsListed;
        stored.TotalCopies = film.TotalCopies;
        stored.AvailableCopies = film.TotalCopies - rented;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FilmUpdateOutcome.Updated;
    }

    public async Task<FilmDeleteOutcome> DeleteAsync(int filmId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Locks.FilmAsync(db, filmId, cancellationToken).ConfigureAwait(false);

        if (!await db.Films.AnyAsync(f => f.Id == filmId, cancellationToken).ConfigureAwait(false))
        {
            return FilmDeleteOutcome.NotFound;
        }

        if (await db.Rentals.AnyAsync(r => r.FilmId == filmId, cancellationToken).ConfigureAwait(false))
        {
            return FilmDeleteOutcome.HasRentals;
        }

        await db.Films.Where(f => f.Id == filmId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FilmDeleteOutcome.Deleted;
    }

    /// <summary>Films matching every set part of <paramref name="filter"/>.</summary>
    internal static IQueryable<Film> Matching(IQueryable<Film> films, FilmFilter filter)
    {
        if (filter.Genre is { } genre)
        {
            films = films.Where(f => f.Genre == genre);
        }

        if (filter.Director is { } director)
        {
            films = films.Where(f => f.Director == director);
        }

        if (filter.Year is { } year)
        {
            films = films.Where(f => f.Year == year);
        }

        return films;
    }

    private static Film Copy(Film film) => new()
    {
        Title = film.Title,
        Genre = film.Genre,
        Year = film.Year,
        Director = film.Director,
        DailyPrice = film.DailyPrice,
        ImageUrl = film.ImageUrl,
        About = film.About,
        TotalCopies = film.TotalCopies,
        AvailableCopies = film.AvailableCopies,
        IsListed = film.IsListed,
    };
}
