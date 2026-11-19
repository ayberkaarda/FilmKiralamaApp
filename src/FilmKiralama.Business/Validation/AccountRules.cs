using System.Globalization;
using System.Text;
using FilmKiralama.Business.Security;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Validation;

/// <summary>Checks and fixed messages for account values, shared by sign-up, setup and the admin's account form.</summary>
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

    public static void RequireValidUserName(string name, bool allowReserved = false)
    {
        if (!InputRules.IsValidUserName(name))
        {
            throw new BusinessRuleException(UserNameInvalid);
        }

        if (!allowReserved && string.Equals(name, ReservedUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(UserNameReserved);
        }
    }

    public static void RequireValidPassword(string password)
    {
        if (password.Length < PasswordPolicy.MinLength || password.Length > PasswordPolicy.MaxLength)
        {
            throw new BusinessRuleException(PasswordInvalid);
        }
    }

    public static string RequireValidEmail(string? email)
    {
        var value = email?.Trim();
        if (!InputRules.IsValidEmail(value))
        {
            throw new BusinessRuleException(EmailInvalid);
        }

        return value!;
    }

    public static string RequireValidPhone(string? phone) =>
        InputRules.NormalizePhone(phone) ?? throw new BusinessRuleException(PhoneInvalid);

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
