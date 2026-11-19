using System.Globalization;

namespace FilmKiralama.Business;

/// <summary>Rental settings (section <c>Rental</c> of appsettings.json).</summary>
public sealed class RentalPolicy
{
    public const string SectionName = "Rental";

    /// <summary>Highest late fee per day the settings may name.</summary>
    public const decimal MaxDailyLateFee = 10_000m;

    /// <summary>Longest rental the settings may allow.</summary>
    public const int MaxAllowedRentalDays = 3650;

    /// <summary>Fee for each day a copy is returned after its due day.</summary>
    public decimal DailyLateFee { get; init; } = 10m;

    /// <summary>Longest rental period a customer can choose, in days.</summary>
    public int MaxRentalDays { get; init; } = 365;

    /// <summary>Problems of the settings in Turkish, naming settings and limits only; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (DailyLateFee < 0m || DailyLateFee > MaxDailyLateFee || decimal.Round(DailyLateFee, 2) != DailyLateFee)
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Rental:DailyLateFee 0 ile {MaxDailyLateFee} arasında, en fazla iki ondalıklı bir tutar olmalıdır."));
        }

        if (MaxRentalDays < 1 || MaxRentalDays > MaxAllowedRentalDays)
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Rental:MaxRentalDays 1 ile {MaxAllowedRentalDays} arasında bir gün sayısı olmalıdır."));
        }

        return problems;
    }
}
