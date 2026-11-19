using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>Films and their stock.</summary>
public interface IFilmRepository
{
    /// <summary>Listed films with at least one copy on the shelf that match <paramref name="filter"/>, ordered by id.</summary>
    Task<IReadOnlyList<Film>> GetCatalogAsync(FilmFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the films matching <paramref name="query"/>, with the totals of their visible ratings
    /// and the number of all matching films, for account <paramref name="actor"/>, which is checked
    /// first. Without <paramref name="asAdmin"/> only listed films are searched (the customer's catalog);
    /// with it every film, and the account must be a stored admin. A page beyond the last is answered
    /// with the last page.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> no longer exists, its session was revoked, or it is no longer an admin with <paramref name="asAdmin"/>.</exception>
    Task<PagedResult<CatalogEntry>> SearchAsync(CatalogQuery query, Actor actor, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Distinct genres, directors and years of all films.</summary>
    Task<FilmFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every film, ordered by id, for the admin <paramref name="actor"/>.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<IReadOnlyList<Film>> GetAllAsync(Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Film <paramref name="filmId"/> as account <paramref name="actor"/> may see it: a listed film for
    /// every account, an unlisted one only while the stored account is an admin. The actor is checked
    /// first, and the stored role is part of the query itself, so a session that still says admin but
    /// whose account no longer is one gets <see langword="null"/>, as for a missing film.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> no longer exists, or its session was revoked.</exception>
    Task<Film?> FindAsync(Actor actor, int filmId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new film with all its copies on the shelf and returns its id. The admin
    /// <paramref name="actor"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<int> AddAsync(Actor actor, Film film, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a film's details and its number of copies under the film's lock, only while the stored
    /// film still has <see cref="Film.Version"/> of <paramref name="film"/> (the version the admin's form
    /// loaded), and raises the version. The copies on the shelf follow the new total; a total below the
    /// copies rented out is refused. The admin <paramref name="actor"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<FilmUpdateOutcome> UpdateAsync(Actor actor, Film film, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a film that has never been rented, under the film's lock. The admin
    /// <paramref name="actor"/> is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer a stored admin, or its session was revoked.</exception>
    Task<FilmDeleteOutcome> DeleteAsync(Actor actor, int filmId, CancellationToken cancellationToken = default);
}

public enum FilmUpdateOutcome
{
    Updated = 0,
    NotFound = 1,

    /// <summary>The new number of copies is below the number rented out; nothing was written.</summary>
    CopiesBelowRented = 2,

    /// <summary>Another admin changed the film since the form loaded it; nothing was written.</summary>
    ChangedElsewhere = 3,
}

public enum FilmDeleteOutcome
{
    Deleted = 0,
    NotFound = 1,

    /// <summary>The film has rentals, active or past; their history keeps the film.</summary>
    HasRentals = 2,
}
