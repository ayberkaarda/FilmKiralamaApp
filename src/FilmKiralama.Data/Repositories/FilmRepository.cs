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

    public async Task<PagedResult<CatalogEntry>> SearchAsync(CatalogQuery query, int actorId, bool asAdmin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actorId, asAdmin, cancellationToken).ConfigureAwait(false);

        var films = Searched(db, query, listedOnly: !asAdmin);
        var total = await films.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = Paging.ClampPage(query.Page, total, query.PageSize);
        var rows = await films
            .OrderBy(f => f.Title)
            .ThenBy(f => f.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new
            {
                Film = f,
                Count = db.Reviews.Count(r => r.FilmId == f.Id && !r.IsHidden),
                Sum = db.Reviews.Where(r => r.FilmId == f.Id && !r.IsHidden).Sum(r => (int?)r.Rating) ?? 0,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResult<CatalogEntry>([.. rows.Select(r => new CatalogEntry(r.Film, new RatingTotals(r.Count, r.Sum)))], total, page, query.PageSize);
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

    public async Task<IReadOnlyList<Film>> GetAllAsync(int adminId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);
        return await db.Films.AsNoTracking().OrderBy(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Film?> FindAsync(int filmId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Films.AsNoTracking().SingleOrDefaultAsync(f => f.Id == filmId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(int adminId, Film film, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(film);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);
        var entity = Copy(film);
        entity.AvailableCopies = film.TotalCopies;
        db.Films.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<FilmUpdateOutcome> UpdateAsync(int adminId, Film film, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(film);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Locks.FilmAsync(db, film.Id, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);

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

    public async Task<FilmDeleteOutcome> DeleteAsync(int adminId, int filmId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Locks.FilmAsync(db, filmId, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, adminId, admin: true, cancellationToken).ConfigureAwait(false);

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

    /// <summary>The films of <paramref name="query"/>, without paging or ordering.</summary>
    private static IQueryable<Film> Searched(FilmDbContext db, CatalogQuery query, bool listedOnly)
    {
        var films = db.Films.AsNoTracking();
        if (listedOnly)
        {
            films = films.Where(f => f.IsListed);
        }

        if (!string.IsNullOrEmpty(query.Title))
        {
            var pattern = "%" + EscapeLike(query.Title) + "%";
            films = films.Where(f => EF.Functions.Like(f.Title, pattern, LikeEscape));
        }

        if (query.Genre is { } genre)
        {
            films = films.Where(f => f.Genre == genre);
        }

        if (query.YearFrom is { } from)
        {
            films = films.Where(f => f.Year >= from);
        }

        if (query.YearTo is { } to)
        {
            films = films.Where(f => f.Year <= to);
        }

        if (query.AvailableOnly)
        {
            films = films.Where(f => f.IsListed && f.AvailableCopies > 0);
        }

        if (query.MinRating is { } minimum)
        {
            // The shown average is rounded to one decimal, halves away from zero, so it is at least
            // "minimum" exactly when sum / count >= minimum - 0.05, that is 20 x sum >= (20 x minimum - 1)
            // x count; whole numbers only, the same on every provider.
            var threshold = (20 * minimum) - 1;
            films = films.Where(f =>
                db.Reviews.Any(r => r.FilmId == f.Id && !r.IsHidden)
                && 20 * db.Reviews.Where(r => r.FilmId == f.Id && !r.IsHidden).Sum(r => r.Rating)
                    >= threshold * db.Reviews.Count(r => r.FilmId == f.Id && !r.IsHidden));
        }

        return films;
    }

    /// <summary>The escape character of the title search.</summary>
    private const string LikeEscape = "\\";

    /// <summary>
    /// <paramref name="text"/> as a LIKE pattern that matches only itself: the escape character, %, _
    /// and [ (a character class on SQL Server) are escaped.
    /// </summary>
    internal static string EscapeLike(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
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
