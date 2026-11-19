namespace FilmKiralama.Domain.Entities;

/// <summary>
/// A customer's rating of a film they rented and returned, with an optional short comment (table
/// Degerlendirmeler). One per account and film; the customer can change it. An admin can hide it, which
/// takes it out of the film's public reviews and average.
/// </summary>
public sealed class Review
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int FilmId { get; set; }

    /// <summary>1 to 5 stars.</summary>
    public int Rating { get; set; }

    /// <summary>Plain text, or <see langword="null"/>; never interpreted as markup.</summary>
    public string? Comment { get; set; }

    /// <summary>Hidden by an admin: not shown to customers and not counted in the average.</summary>
    public bool IsHidden { get; set; }

    /// <summary>UTC time of the last change, by the database's clock.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Number and sum of the visible ratings of a film; the average is worked out by the business layer.</summary>
public readonly record struct RatingTotals(int Count, int Sum)
{
    public static RatingTotals None => default;
}

/// <summary>A review with its film and author, for the lists.</summary>
public sealed record ReviewDetails(
    int Id,
    int FilmId,
    string FilmTitle,
    int UserId,
    string UserName,
    int Rating,
    string? Comment,
    bool IsHidden,
    DateTime UpdatedAt);

/// <summary>A film the account has rented and returned, so it may rate it, with its own rating if any.</summary>
public sealed record ReviewableFilm(int FilmId, string Title, string Genre, int Year, int? ReviewId, int? MyRating);
