using System.Globalization;
using System.Text;
using FilmKiralama.Business.Security;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Validation;

/// <summary>
/// Fixed messages for account values, shared by sign-up, setup and the admin's account form; the
/// validators in <c>Validators.cs</c> apply the rules.
/// </summary>
internal static class AccountRules
{
    internal const string AllFieldsRequired = "Lütfen tüm alanları doldurun.";

    internal const string UserNameReserved = "Bu kullanıcı adı rezerve edilmiştir. Lütfen başka bir kullanıcı adı seçin.";

    internal const string EmailInvalid = "Lütfen geçerli bir e-posta adresi girin.";

    internal const string PasswordsDoNotMatch = "Şifreler uyuşmuyor.";

    internal static readonly string PhoneInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Telefon numarası {InputRules.PhoneDigits} haneden oluşmalıdır.");

    internal static readonly string UserNameInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kullanıcı adı {InputRules.UserNameMinLength}-{InputRules.UserNameMaxLength} karakter olmalı; yalnızca harf, rakam ve . _ - içerebilir.");

    internal static readonly string PasswordInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Şifre en az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter olmalıdır.");

    /// <summary>The name the original sign-up form kept for the shop's own admin account.</summary>
    private const string ReservedUserName = "admin";

    /// <summary>Whether <paramref name="name"/> is the name the sign-up form keeps for the shop's own admin.</summary>
    public static bool IsReserved(string? name) => string.Equals(name, ReservedUserName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The message the original sign-up form showed for values other accounts already use.</summary>
    public static string DescribeTaken(AccountFields taken)
    {
        var message = new StringBuilder("Kayıt yapılamadı. Lütfen aşağıdaki hataları düzeltin:");
        if (taken.HasFlag(AccountFields.UserName))
        {
            message.Append("\n- Bu kullanıcı adı zaten mevcut.");
        }

        if (taken.HasFlag(AccountFields.Email))
        {
            message.Append("\n- Bu e-posta adresi zaten kullanılıyor.");
        }

        if (taken.HasFlag(AccountFields.Phone))
        {
            message.Append("\n- Bu telefon numarası zaten kayıtlı.");
        }

        return message.ToString();
    }
}
