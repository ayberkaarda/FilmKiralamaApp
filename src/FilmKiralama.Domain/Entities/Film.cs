namespace FilmKiralama.Domain.Entities;

/// <summary>A film that can be rented (table Filmler).</summary>
public sealed class Film
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public int Year { get; set; }

    public string Director { get; set; } = string.Empty;

    /// <summary>Price of one rental day.</summary>
    public decimal DailyPrice { get; set; }

    /// <summary>Address of the poster image (http or https), or <see langword="null"/>.</summary>
    public string? ImageUrl { get; set; }

    public string? About { get; set; }

    /// <summary>Copies the shop owns.</summary>
    public int TotalCopies { get; set; }

    /// <summary>
    /// Copies on the shelf: <see cref="TotalCopies"/> minus the active rentals. Only a rental or a
    /// return changes it, under the film's lock.
    /// </summary>
    public int AvailableCopies { get; set; }

    /// <summary>
    /// Whether customers can see and rent the film. The original application hid a film by setting
    /// its status to "Kirada" by hand; such films keep that state as unlisted.
    /// </summary>
    public bool IsListed { get; set; } = true;
}

/// <summary>Distinct values the catalog can be filtered by.</summary>
public sealed record FilmFilterOptions(
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Directors,
    IReadOnlyList<int> Years);

/// <summary>Catalog or rental filter; <see langword="null"/> means any value.</summary>
public sealed record FilmFilter(string? Genre = null, string? Director = null, int? Year = null)
{
    public static FilmFilter None { get; } = new();
}
