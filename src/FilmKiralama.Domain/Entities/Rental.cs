namespace FilmKiralama.Domain.Entities;

/// <summary>A rental of one copy of a film by one account (table Kiralama).</summary>
public sealed class Rental
{
    public int Id { get; set; }

    public int FilmId { get; set; }

    public int UserId { get; set; }

    /// <summary>Local calendar day the rental started.</summary>
    public DateOnly RentedOn { get; set; }

    /// <summary>
    /// Local calendar day the copy is due back. Rentals of the original application may have none;
    /// they are never charged a late fee.
    /// </summary>
    public DateOnly? DueOn { get; set; }

    /// <summary>Local calendar day the copy came back, or <see langword="null"/> while it is out.</summary>
    public DateOnly? ReturnedOn { get; set; }

    public RentalStatus Status { get; set; }

    /// <summary>Price of the rental period, agreed when it started.</summary>
    public decimal Price { get; set; }

    /// <summary>Fee for the days after <see cref="DueOn"/>, set on return; <see langword="null"/> when there is none.</summary>
    public decimal? LateFee { get; set; }
}

/// <summary>State of a rental (column Durum).</summary>
public enum RentalStatus
{
    /// <summary>The copy is out; it holds one of the film's copies.</summary>
    Active = 0,

    /// <summary>The copy came back; the row is history.</summary>
    Returned = 1,
}

/// <summary>A rental with its film and account, for the rental lists.</summary>
public sealed record RentalDetails(
    int Id,
    int FilmId,
    string FilmTitle,
    string Genre,
    int Year,
    string Director,
    string? ImageUrl,
    int UserId,
    string UserName,
    DateOnly RentedOn,
    DateOnly? DueOn,
    decimal Price,
    RentalStatus Status);
