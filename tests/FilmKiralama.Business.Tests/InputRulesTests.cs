using FilmKiralama.Business.Validation;

namespace FilmKiralama.Business.Tests;

public sealed class InputRulesTests
{
    [Theory]
    [InlineData("555 123 4567", "5551234567")]
    [InlineData("5551234567", "5551234567")]
    [InlineData("555123456", null)]
    [InlineData("555-123-4567", null)]
    [InlineData("٥٥٥١٢٣٤٥٦٧", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizePhone_KeepsExactlyTenAsciiDigits(string? typed, string? expected)
    {
        Assert.Equal(expected, InputRules.NormalizePhone(typed));
    }

    [Theory]
    [InlineData("12,50", true, "12.50")]
    [InlineData("12", true, "12")]
    [InlineData(" 9,5 ", true, "9.5")]
    [InlineData("12.50", false, "0")]
    [InlineData("1.250", false, "0")]
    [InlineData("12,505", false, "0")]
    [InlineData("-3", false, "0")]
    [InlineData("", false, "0")]
    public void TryParseMoney_ReadsTheTurkishDecimalCommaOnly(string text, bool ok, string expected)
    {
        Assert.Equal(ok, InputRules.TryParseMoney(text, out var amount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData("ornek", true)]
    [InlineData("ornek.kullanici_1-a", true)]
    [InlineData("Çağrı", true)]
    [InlineData("ab", false)]
    [InlineData("iki kelime", false)]
    [InlineData("tirnak'", false)]
    public void IsValidUserName_LettersDigitsAndSeparators(string name, bool valid)
    {
        Assert.Equal(valid, InputRules.IsValidUserName(name));
    }
}
