namespace FilmKiralama.Domain.Entities;

/// <summary>How often a film was rented in a period.</summary>
public sealed record RentalCountRow(int FilmId, string Title, string Genre, int Rentals);

/// <summary>A rental still out after its due day.</summary>
public sealed record OverdueRentalRow(int RentalId, string FilmTitle, string UserName, DateOnly RentedOn, DateOnly DueOn);

/// <summary>A returned rental that was charged a late fee.</summary>
public sealed record LateFeeRow(int RentalId, string FilmTitle, string UserName, DateOnly DueOn, DateOnly ReturnedOn, decimal LateFee);

/// <summary>How many rentals of one genre an account has made.</summary>
public readonly record struct GenreCount(string Genre, int Rentals);
