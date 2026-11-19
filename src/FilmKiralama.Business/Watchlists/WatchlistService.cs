using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Watchlists;

/// <summary>A film of the customer's watchlist as the list shows it.</summary>
public sealed record WatchlistView(int EntryId, CatalogItem Item, DateOnly AddedOn);

/// <summary>Limits of a watchlist.</summary>
public static class WatchlistPolicy
{
    /// <summary>Films one watchlist can hold.</summary>
    public const int MaxEntries = 100;
}

/// <summary>
/// The customer's own watchlist: films to rent later. Renting a film takes it off the list. Another
/// account's entry gets the answer of a missing one.
/// </summary>
public interface IWatchlistService
{
    /// <summary>Adds a listed film; <see langword="false"/> when it is on the list already.</summary>
    /// <exception cref="BusinessRuleException">The film does not exist (or is unlisted), or the list is full.</exception>
    Task<bool> AddAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the account's own entries.</summary>
    /// <exception cref="BusinessRuleException">No such entry on the account's list.</exception>
    Task RemoveAsync(UserSession session, int entryId, CancellationToken cancellationToken = default);

    /// <summary>The account's list, newest first.</summary>
    Task<IReadOnlyList<WatchlistView>> GetMineAsync(UserSession session, CancellationToken cancellationToken = default);
}

internal sealed class WatchlistService(
    IWatchlistRepository watchlists,
    IClock clock,
    TimeProvider timeProvider,
    IAuditLog audit) : IWatchlistService
{
    internal const string FilmNotFound = "Film bulunamadı.";
    internal const string EntryNotFound = "Bu film izleme listenizde bulunamadı.";

    internal static readonly string ListFull = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"İzleme listeniz dolu; en fazla {WatchlistPolicy.MaxEntries} film ekleyebilirsiniz. Önce listeden film çıkarın.");

    public async Task<bool> AddAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await audit.AsActorAsync(
            session,
            () => watchlists.AddAsync(session.UserId, filmId, now, WatchlistPolicy.MaxEntries, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case WatchlistAddOutcome.Added:
                audit.Record(AuditEvent.WatchlistChanged, AuditCodes.Of(session.Role));
                return true;
            case WatchlistAddOutcome.AlreadyListed:
                return false;
            case WatchlistAddOutcome.ListFull:
                throw new BusinessRuleException(ListFull);
            default:
                throw new BusinessRuleException(FilmNotFound);
        }
    }

    public async Task RemoveAsync(UserSession session, int entryId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var removed = await audit.AsActorAsync(session, () => watchlists.RemoveAsync(session.UserId, entryId, cancellationToken)).ConfigureAwait(false);
        if (!removed)
        {
            throw new BusinessRuleException(EntryNotFound);
        }

        audit.Record(AuditEvent.WatchlistChanged, AuditCodes.Of(session.Role));
    }

    public async Task<IReadOnlyList<WatchlistView>> GetMineAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var items = await audit.AsActorAsync(session, () => watchlists.GetAsync(session.UserId, cancellationToken)).ConfigureAwait(false);
        var zone = timeProvider.LocalTimeZone;
        return
        [
            .. items.Select(i => new WatchlistView(
                i.EntryId,
                CatalogItem.From(new CatalogEntry(i.Film, i.Rating)),
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(i.AddedAt, DateTimeKind.Utc), zone)))),
        ];
    }
}
