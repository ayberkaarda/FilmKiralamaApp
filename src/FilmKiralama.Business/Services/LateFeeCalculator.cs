namespace FilmKiralama.Business.Services;

/// <summary>Rental and late return arithmetic. Only calendar days count.</summary>
internal static class LateFeeCalculator
{
    /// <summary>Largest amount the DECIMAL(10, 2) money columns can hold.</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    /// <summary>Price of <paramref name="days"/> rental days, rounded to kuruş.</summary>
    public static decimal RentalPrice(int days, decimal dailyPrice)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(dailyPrice);
        return decimal.Round(days * dailyPrice, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Whole days between the due day and <paramref name="returnedOn"/>; 0 when the copy comes back on
    /// or before the due day, or when the rental has no due day (rentals of the original application
    /// without one are never charged).
    /// </summary>
    public static int OverdueDays(DateOnly? dueOn, DateOnly returnedOn) =>
        dueOn is { } due ? Math.Max(0, returnedOn.DayNumber - due.DayNumber) : 0;

    /// <summary>
    /// Late fee for <paramref name="overdueDays"/> days, rounded to kuruş and capped at
    /// <see cref="MaxAmount"/>, so a copy that is decades late can still be returned.
    /// </summary>
    public static decimal LateFee(int overdueDays, decimal dailyLateFee)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dailyLateFee);
        if (overdueDays <= 0)
        {
            return 0m;
        }

        return Math.Min(MaxAmount, decimal.Round(overdueDays * dailyLateFee, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>The late fee of a copy due on <paramref name="dueOn"/> and returned on <paramref name="returnedOn"/>.</summary>
    public static decimal LateFee(DateOnly? dueOn, DateOnly returnedOn, decimal dailyLateFee) =>
        LateFee(OverdueDays(dueOn, returnedOn), dailyLateFee);
}
