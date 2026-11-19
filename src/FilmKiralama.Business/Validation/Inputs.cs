using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Business.Validation;

/// <summary>A sign-in attempt. The user name is trimmed; the password is kept as typed.</summary>
internal sealed record SignInInput(string UserName, string? Password)
{
    public static SignInInput Create(string? userName, string? password) =>
        new(userName?.Trim() ?? string.Empty, password);
}

/// <summary>
/// A new account of the sign-up form. The user name and the e-mail address are trimmed; the password
/// and the phone number are kept as typed (the phone may carry the spaces the form inserts).
/// </summary>
internal sealed record SignUpInput(string UserName, string? Password, string Email, string? Phone)
{
    public static SignUpInput Create(string? userName, string? password, string? email, string? phone) =>
        new(userName?.Trim() ?? string.Empty, password, email?.Trim() ?? string.Empty, phone);
}

/// <summary>A change of the signed-in account's own password; every value is kept as typed.</summary>
internal sealed record PasswordChangeInput(string? CurrentPassword, string? NewPassword, string? NewPasswordRepeat);

/// <summary>The first admin of the first-run setup. The user name is trimmed; the passwords are kept as typed.</summary>
internal sealed record FirstAdminInput(string UserName, string? Password, string? PasswordRepeat)
{
    public static FirstAdminInput Create(string? userName, string? password, string? passwordRepeat) =>
        new(userName?.Trim() ?? string.Empty, password, passwordRepeat);
}

/// <summary>
/// An account the admin adds. The user name and the e-mail address are trimmed; the password and the
/// phone number are kept as typed.
/// </summary>
internal sealed record NewAccountInput(string UserName, string? Password, string Email, string? Phone, UserRole Role)
{
    public static NewAccountInput Create(AccountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.UserName?.Trim() ?? string.Empty, input.Password, input.Email?.Trim() ?? string.Empty, input.Phone, input.Role);
    }
}

/// <summary>
/// A change the admin makes to an account. E-mail and phone are <see langword="null"/> when left empty
/// (accounts of the original application have none). <paramref name="KeepsStoredName"/> tells that the
/// name is the account's stored name, which may predate today's rules and is then kept as it is.
/// </summary>
internal sealed record AccountChangeInput(string UserName, string? Password, string? Email, string? Phone, UserRole Role, bool KeepsStoredName)
{
    public static AccountChangeInput Create(AccountInput input, bool keepsStoredName)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(
            input.UserName?.Trim() ?? string.Empty,
            input.Password,
            string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
            string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone,
            input.Role,
            keepsStoredName);
    }
}

/// <summary>
/// The fields of the admin's film form as typed, checked against the latest year allowed
/// (<paramref name="MaxYear"/>, the current one). On a change (<paramref name="IsChange"/>) the
/// description may be empty: the upgrade keeps films of the original application without one.
/// </summary>
internal sealed record FilmFields(
    string? Title,
    string? Genre,
    string? Year,
    string? Director,
    string? DailyPrice,
    string? ImageUrl,
    string? About,
    int Copies,
    int MaxYear,
    bool IsChange)
{
    public static FilmFields Create(FilmInput input, int maxYear, bool isChange)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.Title, input.Genre, input.Year, input.Director, input.DailyPrice, input.ImageUrl, input.About, input.Copies, maxYear, isChange);
    }
}

/// <summary>A rental of <paramref name="Days"/> days at the daily price the customer was shown.</summary>
internal sealed record RentalQuoteInput(int Days, decimal DailyPrice);

/// <summary>
/// A review as typed. The comment is trimmed; an empty one is no comment. <see cref="NormalizedComment"/>
/// is the text that is stored once the comment is valid (Unicode normalization form C).
/// </summary>
internal sealed record ReviewFields(int Rating, string? Comment)
{
    public string? NormalizedComment => Comment is null ? null : Comment.Normalize(System.Text.NormalizationForm.FormC);

    public static ReviewFields Create(Reviews.ReviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.Rating, string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim());
    }
}
