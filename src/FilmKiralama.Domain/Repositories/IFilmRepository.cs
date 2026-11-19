using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>Films and their stock.</summary>
public interface IFilmRepository
{
    /// <summary>Listed films with at least one copy on the shelf that match <paramref name="filter"/>, ordered by id.</summary>
    Task<IReadOnlyList<Film>> GetCatalogAsync(FilmFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Distinct genres, directors and years of all films.</summary>
    Task<FilmFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every film, ordered by id, for the admin <paramref name="adminId"/>.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<IReadOnlyList<Film>> GetAllAsync(int adminId, CancellationToken cancellationToken = default);

    Task<Film?> FindAsync(int filmId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new film with all its copies on the shelf and returns its id. The admin
    /// <paramref name="adminId"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<int> AddAsync(int adminId, Film film, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a film's details and its number of copies under the film's lock. The copies on the
    /// shelf follow the new total; a total below the copies rented out is refused. The admin
    /// <paramref name="adminId"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<FilmUpdateOutcome> UpdateAsync(int adminId, Film film, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a film that has never been rented, under the film's lock. The admin
    /// <paramref name="adminId"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="adminId"/> is no longer a stored admin.</exception>
    Task<FilmDeleteOutcome> DeleteAsync(int adminId, int filmId, CancellationToken cancellationToken = default);
}

public enum FilmUpdateOutcome
{
    Updated = 0,
    NotFound = 1,

    /// <summary>The new number of copies is below the number rented out; nothing was written.</summary>
    CopiesBelowRented = 2,
}

public enum FilmDeleteOutcome
{
    Deleted = 0,
    NotFound = 1,

    /// <summary>The film has rentals, active or past; their history keeps the film.</summary>
    HasRentals = 2,
}
