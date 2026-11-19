namespace FilmKiralama.Domain.Entities;

/// <summary>
/// A film a customer keeps on their watchlist to rent later (table IzlemeListesi); once per account and
/// film. Renting the film takes it off the list.
/// </summary>
public sealed class WatchlistEntry
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int FilmId { get; set; }

    /// <summary>UTC time the film was added, by the database's clock.</summary>
    public DateTime AddedAt { get; set; }
}

/// <summary>A watchlist entry with its film and the totals of the film's visible ratings.</summary>
public sealed record WatchlistItem(int EntryId, Film Film, RatingTotals Rating, DateTime AddedAt);
