using System.Globalization;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FluentValidation;

namespace FilmKiralama.Business.Validation;

// The user facing (Turkish) messages are the ones the services showed before; they name fields and
// limits only, never the entered values. The account validators stop at their first failure, so the user
// sees one clear message at a time, in the order the services checked the rules. The film validator
// checks every field and reports one message per invalid field, as the original form marked each one.

internal static class ValidationRuleExtensions
{
    /// <summary>Validates <paramref name="input"/> and throws a <see cref="BusinessRuleException"/> carrying the first failure message.</summary>
    public static void ValidateOrThrow<T>(this IValidator<T> validator, T input)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(input);
        if (!result.IsValid)
        {
            throw new BusinessRuleException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>A new password of <see cref="PasswordPolicy.MinLength"/> to <see cref="PasswordPolicy.MaxLength"/> characters.</summary>
    public static bool IsAcceptableNewPassword(string? password) =>
        password is { Length: >= PasswordPolicy.MinLength and <= PasswordPolicy.MaxLength };

    public static bool IsValidPhone(string? phone) => InputRules.NormalizePhone(phone) is not null;
}

/// <summary>
/// Sign-in: a name and a password are given (<see cref="Required"/>), and both fit what an existing
/// account can hold (<see cref="Unacceptable"/>). The length rules of <see cref="PasswordPolicy"/> apply
/// to new passwords only; here they would lock out accounts of the original application.
/// </summary>
internal sealed class SignInInputValidator : AbstractValidator<SignInInput>
{
    /// <summary>Error code of a missing name or password.</summary>
    public const string Required = "required";

    /// <summary>Error code of a name or password no account can have; refused like a wrong password.</summary>
    public const string Unacceptable = "unacceptable";

    public SignInInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(s => s)
            .Must(s => s.UserName.Length > 0 && !string.IsNullOrEmpty(s.Password))
            .WithName(nameof(SignInInput))
            .WithErrorCode(Required)
            .WithMessage(AuthService.InputRequired);
        RuleFor(s => s.UserName)
            .MaximumLength(InputRules.StoredUserNameMaxLength)
            .WithErrorCode(Unacceptable)
            .WithMessage(_ => AuthService.CredentialsInvalid);
        RuleFor(s => s.Password)
            .Must(p => p!.Length <= PasswordPolicy.MaxLength)
            .WithErrorCode(Unacceptable)
            .WithMessage(_ => AuthService.CredentialsInvalid);
    }
}

/// <summary>
/// The sign-up form: every field given, a user name of the allowed shape that is not the reserved one, a
/// password of the policy's length, an e-mail address and a ten digit phone number.
/// </summary>
internal sealed class SignUpInputValidator : AbstractValidator<SignUpInput>
{
    public SignUpInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(s => s)
            .Must(s => s.UserName.Length > 0 && !string.IsNullOrWhiteSpace(s.Password) && s.Email.Length > 0 && !string.IsNullOrWhiteSpace(s.Phone))
            .WithName(nameof(SignUpInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(s => s.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid)
            .Must(name => !AccountRules.IsReserved(name)).WithMessage(AccountRules.UserNameReserved);
        RuleFor(s => s.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
        RuleFor(s => s.Email)
            .Must(InputRules.IsValidEmail).WithMessage(AccountRules.EmailInvalid);
        RuleFor(s => s.Phone)
            .Must(ValidationRuleExtensions.IsValidPhone).WithMessage(_ => AccountRules.PhoneInvalid);
    }
}

/// <summary>A password change: every field given, the new password twice the same and of the policy's length.</summary>
internal sealed class PasswordChangeInputValidator : AbstractValidator<PasswordChangeInput>
{
    public PasswordChangeInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(p => p)
            .Must(p => !string.IsNullOrWhiteSpace(p.CurrentPassword) && !string.IsNullOrWhiteSpace(p.NewPassword) && !string.IsNullOrWhiteSpace(p.NewPasswordRepeat))
            .WithName(nameof(PasswordChangeInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(p => p.NewPasswordRepeat)
            .Must((p, repeat) => string.Equals(p.NewPassword, repeat, StringComparison.Ordinal)).WithMessage(AuthService.NewPasswordsDoNotMatch);
        RuleFor(p => p.NewPassword)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// The first admin: every field given, a user name of the allowed shape (the shop's own admin may be
/// called "admin"), the password twice the same and of the policy's length.
/// </summary>
internal sealed class FirstAdminInputValidator : AbstractValidator<FirstAdminInput>
{
    public FirstAdminInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => a.UserName.Length > 0 && !string.IsNullOrWhiteSpace(a.Password) && !string.IsNullOrWhiteSpace(a.PasswordRepeat))
            .WithName(nameof(FirstAdminInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.PasswordRepeat)
            .Must((a, repeat) => string.Equals(a.Password, repeat, StringComparison.Ordinal)).WithMessage(AccountRules.PasswordsDoNotMatch);
        RuleFor(a => a.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// An account the admin adds: every field given and a known role, a user name of the allowed shape (the
/// reserved one included), an e-mail address, a ten digit phone number and a password of the policy's length.
/// </summary>
internal sealed class NewAccountInputValidator : AbstractValidator<NewAccountInput>
{
    public NewAccountInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => !string.IsNullOrWhiteSpace(a.Password) && a.UserName.Length > 0 && a.Email.Length > 0
                && !string.IsNullOrWhiteSpace(a.Phone) && Enum.IsDefined(a.Role))
            .WithName(nameof(NewAccountInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must(InputRules.IsValidUserName).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.Email)
            .Must(InputRules.IsValidEmail).WithMessage(AccountRules.EmailInvalid);
        RuleFor(a => a.Phone)
            .Must(ValidationRuleExtensions.IsValidPhone).WithMessage(_ => AccountRules.PhoneInvalid);
        RuleFor(a => a.Password)
            .Must(ValidationRuleExtensions.IsAcceptableNewPassword).WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// A change of an account: a name and a known role, a name of the allowed shape unless it is the
/// account's stored one, e-mail and phone checked only when given, and a new password only when given.
/// </summary>
internal sealed class AccountChangeInputValidator : AbstractValidator<AccountChangeInput>
{
    public AccountChangeInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a)
            .Must(a => a.UserName.Length > 0 && Enum.IsDefined(a.Role))
            .WithName(nameof(AccountChangeInput))
            .WithMessage(AccountRules.AllFieldsRequired);
        RuleFor(a => a.UserName)
            .Must((a, name) => a.KeepsStoredName || InputRules.IsValidUserName(name)).WithMessage(_ => AccountRules.UserNameInvalid);
        RuleFor(a => a.Email)
            .Must(email => email is null || InputRules.IsValidEmail(email)).WithMessage(AccountRules.EmailInvalid);
        RuleFor(a => a.Phone)
            .Must(phone => phone is null || ValidationRuleExtensions.IsValidPhone(phone)).WithMessage(_ => AccountRules.PhoneInvalid);
        RuleFor(a => a.Password)
            .Must(password => string.IsNullOrEmpty(password) || ValidationRuleExtensions.IsAcceptableNewPassword(password))
            .WithMessage(_ => AccountRules.PasswordInvalid);
    }
}

/// <summary>
/// The film form: title, genre, director and description given and within their column sizes, a year
/// from <see cref="InputRules.MinYear"/> to the current one, a positive daily price with at most two
/// decimals, an optional http or https poster address and <see cref="InputRules.MinCopies"/> to
/// <see cref="InputRules.MaxCopies"/> copies. Every field is checked; each invalid one gets one message
/// under its <see cref="FilmInput"/> property name.
/// </summary>
internal sealed class FilmFieldsValidator : AbstractValidator<FilmFields>
{
    internal const string YearRequired = "Yıl boş bırakılamaz.";
    internal const string PriceRequired = "Ücret boş bırakılamaz.";
    internal const string PriceInvalid = "Ücret sıfırdan büyük, en fazla iki ondalıklı bir tutar olmalıdır (örnek: 12,50).";
    internal const string ImageUrlInvalid = "Resim linki http:// veya https:// ile başlayan, en fazla 255 karakterlik bir adres olmalıdır.";

    internal static readonly string CopiesInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Adet {InputRules.MinCopies} ile {InputRules.MaxCopies} arasında olmalıdır.");

    public FilmFieldsValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Continue;
        RuleLevelCascadeMode = CascadeMode.Stop;

        Text(RuleFor(f => f.Title), "Ad", InputRules.TitleMaxLength);
        Text(RuleFor(f => f.Genre), "Tür", InputRules.GenreMaxLength);
        Text(RuleFor(f => f.Director), "Yönetmen", InputRules.DirectorMaxLength);
        When(f => !(f.IsChange && string.IsNullOrWhiteSpace(f.About)), () => Text(RuleFor(f => f.About), "Hakkında kısmı", InputRules.AboutMaxLength));

        RuleFor(f => f.Year)
            .Must(year => !string.IsNullOrEmpty(year?.Trim())).WithMessage(YearRequired)
            .Must((f, year) => ParseYear(year) is { } value && value >= InputRules.MinYear && value <= f.MaxYear)
            .WithMessage(f => YearInvalid(f.MaxYear));

        RuleFor(f => f.DailyPrice)
            .Must(price => !string.IsNullOrWhiteSpace(price)).WithMessage(PriceRequired)
            .Must(price => InputRules.TryParseMoney(price, out var amount) && amount > 0m && amount <= InputRules.MaxDailyPrice)
            .WithMessage(PriceInvalid);

        RuleFor(f => f.ImageUrl)
            .Must(url => InputRules.IsValidImageUrl(url!.Trim()))
            .When(f => !string.IsNullOrWhiteSpace(f.ImageUrl))
            .WithMessage(ImageUrlInvalid);

        RuleFor(f => f.Copies)
            .InclusiveBetween(InputRules.MinCopies, InputRules.MaxCopies)
            .WithMessage(_ => CopiesInvalid);
    }

    internal static string YearInvalid(int maxYear) =>
        string.Create(CultureInfo.InvariantCulture, $"Yıl {InputRules.MinYear} ile {maxYear} arasında olmalıdır.");

    /// <summary>The year typed as plain digits, or <see langword="null"/>.</summary>
    internal static int? ParseYear(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : null;

    private static void Text(IRuleBuilderInitial<FilmFields, string?> rule, string label, int maxLength) =>
        rule
            .Must(value => !string.IsNullOrEmpty(value?.Trim())).WithMessage(label + " boş bırakılamaz.")
            .Must(value => value!.Trim().Length <= maxLength)
            .WithMessage(string.Create(CultureInfo.InvariantCulture, $"{label} en fazla {maxLength} karakter olabilir."));
}

/// <summary>
/// A catalog search: a title of at most <see cref="InputRules.TitleMaxLength"/> characters of plain text,
/// years from <see cref="InputRules.MinYear"/> to <see cref="CatalogQueryValidator.MaxYear"/> in order, a
/// minimum rating of one to five stars and a page of 1 or more with 1 to
/// <see cref="CatalogQuery.MaxPageSize"/> films.
/// </summary>
internal sealed class CatalogQueryValidator : AbstractValidator<CatalogQuery>
{
    /// <summary>Latest year a search may name; far enough ahead for any film entered.</summary>
    internal const int MaxYear = 9999;

    internal const string TitleInvalid = "Aranan ad görünmeyen veya kontrol karakterleri içeremez.";
    internal const string YearsReversed = "Başlangıç yılı bitiş yılından büyük olamaz.";
    internal const string RatingInvalid = "En düşük puan 1 ile 5 arasında olmalıdır.";
    internal const string PageInvalid = "Sayfa numarası 1 veya daha büyük olmalıdır.";

    internal static readonly string TitleTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Aranan ad en fazla {InputRules.TitleMaxLength} karakter olabilir.");

    internal static readonly string YearInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Yıl {InputRules.MinYear} ile {MaxYear} arasında olmalıdır.");

    internal static readonly string PageSizeInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Sayfa boyutu 1 ile {CatalogQuery.MaxPageSize} arasında olmalıdır.");

    public CatalogQueryValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(q => q.Title)
            .Must(t => t!.Length <= InputRules.TitleMaxLength).WithMessage(_ => TitleTooLong)
            .Must(t => InputRules.IsPlainText(t!)).WithMessage(TitleInvalid)
            .When(q => q.Title is not null);
        RuleFor(q => q.Genre)
            .Must(g => g!.Length <= InputRules.GenreMaxLength).WithMessage(_ => TitleTooLong)
            .When(q => q.Genre is not null);
        RuleFor(q => q.YearFrom)
            .InclusiveBetween(InputRules.MinYear, MaxYear).WithMessage(_ => YearInvalid)
            .When(q => q.YearFrom is not null);
        RuleFor(q => q.YearTo)
            .InclusiveBetween(InputRules.MinYear, MaxYear).WithMessage(_ => YearInvalid)
            .When(q => q.YearTo is not null);
        RuleFor(q => q)
            .Must(q => q.YearFrom is null || q.YearTo is null || q.YearFrom <= q.YearTo)
            .WithName(nameof(CatalogQuery))
            .WithMessage(YearsReversed);
        RuleFor(q => q.MinRating)
            .InclusiveBetween(Reviews.ReviewPolicy.MinRating, Reviews.ReviewPolicy.MaxRating).WithMessage(RatingInvalid)
            .When(q => q.MinRating is not null);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage(PageInvalid);
        RuleFor(q => q.PageSize).InclusiveBetween(1, CatalogQuery.MaxPageSize).WithMessage(_ => PageSizeInvalid);
    }
}

/// <summary>A report period: the first day not after the last, at most <see cref="Reports.ReportPolicy.MaxRangeDays"/> days.</summary>
internal sealed class ReportRangeValidator : AbstractValidator<Reports.ReportRange>
{
    internal const string Reversed = "Başlangıç tarihi bitiş tarihinden sonra olamaz.";

    internal static readonly string TooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Rapor en fazla {Reports.ReportPolicy.MaxRangeDays} günü kapsayabilir.");

    public ReportRangeValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r)
            .Must(r => r.From <= r.To).WithName(nameof(Reports.ReportRange)).WithMessage(Reversed)
            .Must(r => r.To.DayNumber - r.From.DayNumber < Reports.ReportPolicy.MaxRangeDays).WithName(nameof(Reports.ReportRange)).WithMessage(_ => TooLong);
    }
}

/// <summary>
/// A review: one to five stars and, optionally, a comment of at most
/// <see cref="Reviews.ReviewPolicy.MaxCommentLength"/> characters of plain text.
/// </summary>
internal sealed class ReviewFieldsValidator : AbstractValidator<ReviewFields>
{
    internal const string RatingInvalid = "Puan 1 ile 5 arasında olmalıdır.";
    internal const string CommentInvalid = "Yorum satır sonu, kontrol veya görünmeyen biçim karakterleri içeremez.";

    internal static readonly string CommentTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Yorum en fazla {Reviews.ReviewPolicy.MaxCommentLength} karakter olabilir.");

    public ReviewFieldsValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.Rating)
            .InclusiveBetween(Reviews.ReviewPolicy.MinRating, Reviews.ReviewPolicy.MaxRating).WithMessage(RatingInvalid);
        RuleFor(r => r.Comment)
            .Must(c => InputRules.IsPlainText(c!)).WithMessage(CommentInvalid)
            .Must(c => c!.Normalize(System.Text.NormalizationForm.FormC).Length <= Reviews.ReviewPolicy.MaxCommentLength).WithMessage(_ => CommentTooLong)
            .When(r => r.Comment is not null);
    }
}

/// <summary>
/// A rental: 1 to <see cref="RentalPolicy.MaxRentalDays"/> days, a positive daily price (a film without
/// one cannot be rented) and a total the money column can hold.
/// </summary>
internal sealed class RentalQuoteInputValidator : AbstractValidator<RentalQuoteInput>
{
    public RentalQuoteInputValidator(RentalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        var daysInvalid = DaysInvalid(policy);
        RuleFor(q => q.Days)
            .InclusiveBetween(1, policy.MaxRentalDays).WithMessage(daysInvalid);
        RuleFor(q => q.DailyPrice)
            .GreaterThan(0m).WithMessage(RentalService.FilmNotFound);
        RuleFor(q => q)
            .Must(q => LateFeeCalculator.RentalPrice(q.Days, q.DailyPrice) <= LateFeeCalculator.MaxAmount)
            .WithName(nameof(RentalQuoteInput))
            .WithMessage(RentalService.TotalTooHigh);
    }

    internal static string DaysInvalid(RentalPolicy policy) =>
        string.Create(CultureInfo.InvariantCulture, $"Gün sayısı 1 ile {policy.MaxRentalDays} arasında olmalıdır.");
}
