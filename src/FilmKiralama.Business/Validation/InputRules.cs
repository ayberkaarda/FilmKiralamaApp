using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FilmKiralama.Business.Validation;

/// <summary>
/// Input rules that the original forms checked partly while typing (digits only, the phone format),
/// now enforced by the services so that no screen can skip them.
/// </summary>
public static partial class InputRules
{
    /// <summary>Shortest and longest user name a new account can choose.</summary>
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 32;

    /// <summary>Longest user name an existing account can have (column size of the original application).</summary>
    public const int StoredUserNameMaxLength = 50;

    public const int EmailMaxLength = 100;

    /// <summary>Phone numbers are ten digits, as the original forms formatted them ("555 123 4567").</summary>
    public const int PhoneDigits = 10;

    public const int TitleMaxLength = 50;
    public const int GenreMaxLength = 50;
    public const int DirectorMaxLength = 50;
    public const int ImageUrlMaxLength = 255;
    public const int AboutMaxLength = 4000;

    /// <summary>First year the original film form offered.</summary>
    public const int MinYear = 1900;

    public const int MinCopies = 1;
    public const int MaxCopies = 1000;

    /// <summary>Largest daily price the DECIMAL(10, 2) column can hold.</summary>
    public const decimal MaxDailyPrice = 99_999_999.99m;

    /// <summary>Money is entered and shown the Turkish way, with a decimal comma (12,50).</summary>
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// User names are letters, digits and the separators . _ - (no spaces). The original sign-up form
    /// accepted anything; existing accounts keep their names and sign in with them.
    /// </summary>
    public static bool IsValidUserName(string? value) =>
        value is { Length: >= UserNameMinLength and <= UserNameMaxLength }
        && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>The e-mail shape something@something.something, no spaces.</summary>
    public static bool IsValidEmail([NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= EmailMaxLength && EmailPattern().IsMatch(value);

    /// <summary>
    /// The digits of a phone number typed with or without the spaces the original forms inserted;
    /// <see langword="null"/> when it is not exactly ten digits.
    /// </summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        return digits.Length == PhoneDigits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    /// <summary>
    /// A price typed with a decimal comma and at most two decimals (no thousands separators, so "1.250"
    /// is refused instead of being read as 1250).
    /// </summary>
    public static bool TryParseMoney(string? value, out decimal amount)
    {
        amount = 0m;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, Turkish, out var parsed)
            || decimal.Round(parsed, 2) != parsed)
        {
            return false;
        }

        amount = parsed;
        return true;
    }

    /// <summary>An absolute http or https address, as the posters are loaded from the web.</summary>
    public static bool IsValidImageUrl([NotNullWhen(true)] string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= ImageUrlMaxLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailPattern();
}
