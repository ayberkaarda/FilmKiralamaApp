using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Domain.Repositories;

/// <summary>Watchlists. Every account reaches only its own list; another account's entry is the same as none.</summary>
public interface IWatchlistRepository
{
    /// <summary>
    /// Adds a listed film to the list of <paramref name="userId"/> under the account's watchlist lock,
    /// unless it is there already or the list holds <paramref name="maxEntries"/> films. Two adds of the
    /// same film at once leave one entry. The account is checked in the same transaction.
    /// </summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<WatchlistAddOutcome> AddAsync(int userId, int filmId, DateTime now, int maxEntries, CancellationToken cancellationToken = default);

    /// <summary>Removes entry <paramref name="entryId"/> when it belongs to <paramref name="userId"/>.</summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<bool> RemoveAsync(int userId, int entryId, CancellationToken cancellationToken = default);

    /// <summary>The account's list, newest first, with the films as they are now (unlisted ones included).</summary>
    /// <exception cref="ActorRevokedException">The account no longer exists.</exception>
    Task<IReadOnlyList<WatchlistItem>> GetAsync(int userId, CancellationToken cancellationToken = default);
}

public enum WatchlistAddOutcome
{
    Added = 0,

    /// <summary>The film is on the list already; nothing was written.</summary>
    AlreadyListed = 1,

    /// <summary>The film does not exist or is not listed for customers; nothing was written.</summary>
    FilmNotFound = 2,

    /// <summary>The list is full; nothing was written.</summary>
    ListFull = 3,
}
