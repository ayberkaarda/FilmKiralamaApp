using FilmKiralama.Business.Services;

namespace FilmKiralama.Business.Tests;

public sealed class LateFeeCalculatorTests
{
    private static readonly DateOnly Due = new(2026, 3, 10);

    [Theory]
    [InlineData(1, "12.50", "12.50")]
    [InlineData(3, "12.50", "37.50")]
    [InlineData(7, "9.99", "69.93")]
    [InlineData(365, "0.01", "3.65")]
    public void RentalPrice_IsDaysTimesTheDailyPrice(int days, string dailyPrice, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), LateFeeCalculator.RentalPrice(days, decimal.Parse(dailyPrice, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void RentalPrice_RefusesLessThanOneDay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LateFeeCalculator.RentalPrice(0, 10m));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    public void OverdueDays_CountsOnlyCalendarDaysAfterTheDueDay(int offset, int expected)
    {
        Assert.Equal(expected, LateFeeCalculator.OverdueDays(Due, Due.AddDays(offset)));
    }

    [Fact]
    public void OverdueDays_RentalWithoutDueDay_IsNeverLate()
    {
        Assert.Equal(0, LateFeeCalculator.OverdueDays(null, Due.AddDays(400)));
        Assert.Equal(0m, LateFeeCalculator.LateFee(null, Due.AddDays(400), 10m));
    }

    [Fact]
    public void LateFee_IsOverdueDaysTimesTheDailyFeeInDecimal()
    {
        Assert.Equal(0m, LateFeeCalculator.LateFee(Due, Due, 10m));
        Assert.Equal(10m, LateFeeCalculator.LateFee(Due, Due.AddDays(1), 10m));
        Assert.Equal(22.5m, LateFeeCalculator.LateFee(Due, Due.AddDays(3), 7.5m));

        // 0.1 is not exact in binary floating point; in decimal three days are exactly 0.30.
        Assert.Equal(0.30m, LateFeeCalculator.LateFee(Due, Due.AddDays(3), 0.10m));
    }

    [Fact]
    public void LateFee_ZeroDailyFee_ChargesNothing()
    {
        Assert.Equal(0m, LateFeeCalculator.LateFee(Due, Due.AddDays(9), 0m));
    }

    [Fact]
    public void LateFee_IsCappedAtTheLargestStorableAmount()
    {
        Assert.Equal(LateFeeCalculator.MaxAmount, LateFeeCalculator.LateFee(int.MaxValue, 10_000m));
    }

    [Fact]
    public void LateFee_RefusesANegativeDailyFee()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LateFeeCalculator.LateFee(3, -1m));
    }
}
