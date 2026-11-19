namespace FilmKiralama.Domain.Entities;

/// <summary>
/// A search of the film list: every set part has to match. <see cref="Title"/> is found anywhere in the
/// title (its %, _ and [ characters are matched as themselves), <see cref="Genre"/> exactly, the year
/// from <see cref="YearFrom"/> to <see cref="YearTo"/> inclusive, <see cref="MinRating"/> against the
/// shown (rounded) average of the visible ratings, and <see cref="AvailableOnly"/> keeps films with a
/// copy on the shelf. The list is ordered by title and then by id, so pages never overlap or skip a film.
/// </summary>
public sealed record CatalogQuery(
    string? Title = null,
    string? Genre = null,
    int? YearFrom = null,
    int? YearTo = null,
    int? MinRating = null,
    bool AvailableOnly = false,
    int Page = 1,
    int PageSize = CatalogQuery.DefaultPageSize)
{
    /// <summary>Films per page of the customer's catalog.</summary>
    public const int DefaultPageSize = 12;

    /// <summary>Largest page any screen may ask for.</summary>
    public const int MaxPageSize = 50;
}

/// <summary>A film of the catalog with the totals of its visible ratings.</summary>
public sealed record CatalogEntry(Film Film, RatingTotals Rating);
