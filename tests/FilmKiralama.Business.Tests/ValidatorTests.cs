using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FluentValidation;

namespace FilmKiralama.Business.Tests;

/// <summary>
/// The validators on their own: boundaries, the order of the messages and the Turkish texts the
/// screens showed before the rules moved into the validators.
/// </summary>
public sealed class ValidatorTests
{
    private const string Password = "Gizli.Sifre1";

    [Fact]
    public void Messages_AreTheTextsTheScreensShowed()
    {
        Assert.Equal("Lütfen kullanıcı adı ve şifrenizi girin.", AuthService.InputRequired);
        Assert.Equal("Kullanıcı adı veya şifre hatalı. 5 hatalı denemeden sonra giriş 15 dakika engellenir.", AuthService.CredentialsInvalid);
        Assert.Equal("Lütfen tüm alanları doldurun.", AccountRules.AllFieldsRequired);
        Assert.Equal("Kullanıcı adı 3-32 karakter olmalı; yalnızca harf, rakam ve . _ - içerebilir.", AccountRules.UserNameInvalid);
        Assert.Equal("Bu kullanıcı adı rezerve edilmiştir. Lütfen başka bir kullanıcı adı seçin.", AccountRules.UserNameReserved);
        Assert.Equal("Şifre en az 8, en fazla 64 karakter olmalıdır.", AccountRules.PasswordInvalid);
        Assert.Equal("Lütfen geçerli bir e-posta adresi girin.", AccountRules.EmailInvalid);
        Assert.Equal("Telefon numarası 10 haneden oluşmalıdır.", AccountRules.PhoneInvalid);
        Assert.Equal("Şifreler uyuşmuyor.", AccountRules.PasswordsDoNotMatch);
        Assert.Equal("Yeni şifreler birbirini tutmuyor. Lütfen aynı şifreyi girin.", AuthService.NewPasswordsDoNotMatch);
        Assert.Equal("Yıl 1900 ile 2026 arasında olmalıdır.", FilmFieldsValidator.YearInvalid(2026));
        Assert.Equal("Adet 1 ile 1000 arasında olmalıdır.", FilmFieldsValidator.CopiesInvalid);
        Assert.Equal("Gün sayısı 1 ile 30 arasında olmalıdır.", RentalQuoteInputValidator.DaysInvalid(new RentalPolicy { MaxRentalDays = 30 }));
    }

    [Theory]
    [InlineData("", Password, AuthService.InputRequired, SignInInputValidator.Required)]
    [InlineData("ornek", "", AuthService.InputRequired, SignInInputValidator.Required)]
    [InlineData("ornek", null, AuthService.InputRequired, SignInInputValidator.Required)]
    public void SignIn_MissingValue_AsksForBoth(string userName, string? password, string message, string code)
    {
        var error = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create(userName, password)).Errors);

        Assert.Equal(message, error.ErrorMessage);
        Assert.Equal(code, error.ErrorCode);
    }

    /// <summary>Sign-in accepts what an existing account can hold, also passwords shorter than today's minimum.</summary>
    [Fact]
    public void SignIn_Bounds_AreThoseOfStoredAccounts()
    {
        var longestName = new string('a', InputRules.StoredUserNameMaxLength);
        var longestPassword = new string('x', PasswordPolicy.MaxLength);

        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create(longestName, "1")).IsValid);
        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create("ornek", longestPassword)).IsValid);
        Assert.True(TestValidators.SignIn.Validate(SignInInput.Create("ornek", "   ")).IsValid);

        var name = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create(longestName + "a", Password)).Errors);
        var password = Assert.Single(TestValidators.SignIn.Validate(SignInInput.Create("ornek", longestPassword + "x")).Errors);
        Assert.All(new[] { name, password }, e =>
        {
            Assert.Equal(AuthService.CredentialsInvalid, e.ErrorMessage);
            Assert.Equal(SignInInputValidator.Unacceptable, e.ErrorCode);
        });
    }

    [Fact]
    public void SignIn_TrimsTheNameOnly()
    {
        var input = SignInInput.Create("  ornek ", " sifre ");

        Assert.Equal("ornek", input.UserName);
        Assert.Equal(" sifre ", input.Password);
    }

    [Theory]
    [InlineData(null, Password, "a@example.com", "5551234567", "required")]
    [InlineData("yeni", "   ", "a@example.com", "5551234567", "required")]
    [InlineData("yeni", Password, " ", "5551234567", "required")]
    [InlineData("yeni", Password, "a@example.com", "", "required")]
    [InlineData("ab", "kisa", "eposta", "1", "name")]
    [InlineData("Admin", "kisa", "eposta", "1", "reserved")]
    [InlineData("yeni", "kisa", "eposta", "1", "password")]
    [InlineData("yeni", Password, "eposta", "1", "email")]
    [InlineData("yeni", Password, "a@example.com", "555 123 456", "phone")]
    public void SignUp_ReportsTheFirstBrokenRuleInTheOriginalOrder(string? name, string? password, string? email, string? phone, string rule)
    {
        var error = Assert.Single(TestValidators.SignUp.Validate(SignUpInput.Create(name, password, email, phone)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("a234567890123456789012345678901b", true)]
    [InlineData("a234567890123456789012345678901bc", false)]
    [InlineData("ad.soyad_1-2", true)]
    [InlineData("ad soyad", false)]
    public void SignUp_UserNameLengthAndShape(string name, bool valid)
    {
        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create(name, Password, "a@example.com", "5551234567")).IsValid);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void PasswordPolicy_EightToSixtyFourCharacters(int length, bool valid)
    {
        var password = new string('s', length);

        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create("yeni", password, "a@example.com", "5551234567")).IsValid);
        Assert.Equal(valid, TestValidators.FirstAdmin.Validate(FirstAdminInput.Create("kurucu", password, password)).IsValid);
        Assert.Equal(valid, TestValidators.PasswordChange.Validate(new PasswordChangeInput(Password, password, password)).IsValid);
        Assert.Equal(valid, TestValidators.NewAccount.Validate(NewAccountInput.Create(new AccountInput("yeni", password, "a@example.com", "5551234567", UserRole.User))).IsValid);
        Assert.Equal(valid, TestValidators.AccountChange.Validate(AccountChangeInput.Create(new AccountInput("yeni", password, null, null, UserRole.User), false)).IsValid);
    }

    [Theory]
    [InlineData("a@b.c", true)]
    [InlineData("ad.soyad@ornek.com.tr", true)]
    [InlineData("eposta", false)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.d", false)]
    [InlineData("a@@b.c", false)]
    public void SignUp_EmailFormat(string email, bool valid)
    {
        Assert.Equal(valid, TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, email, "5551234567")).IsValid);
    }

    [Fact]
    public void SignUp_EmailOfTheColumnSize_IsTheLongestAccepted()
    {
        var local = new string('a', InputRules.EmailMaxLength - "@b.co".Length);

        Assert.True(TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, local + "@b.co", "5551234567")).IsValid);
        Assert.False(TestValidators.SignUp.Validate(SignUpInput.Create("yeni", Password, local + "a@b.co", "5551234567")).IsValid);
    }

    [Theory]
    [InlineData("", Password, Password, "required")]
    [InlineData("kurucu", Password, " ", "required")]
    [InlineData("k", "kisa", "baska", "name")]
    [InlineData("kurucu", Password, "Baska.Sifre1", "match")]
    [InlineData("kurucu", "kisa", "kisa", "password")]
    public void FirstAdmin_ReportsTheFirstBrokenRule(string name, string password, string repeat, string rule)
    {
        var error = Assert.Single(TestValidators.FirstAdmin.Validate(FirstAdminInput.Create(name, password, repeat)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Fact]
    public void FirstAdmin_MayBeCalledAdmin() =>
        Assert.True(TestValidators.FirstAdmin.Validate(FirstAdminInput.Create("admin", Password, Password)).IsValid);

    [Theory]
    [InlineData(" ", "Yeni.Sifre12", "Yeni.Sifre12", "required")]
    [InlineData(Password, "Yeni.Sifre12", "Yeni.Sifre13", "new-match")]
    [InlineData(Password, "kisa", "kisa", "password")]
    public void PasswordChange_ReportsTheFirstBrokenRule(string current, string next, string repeat, string rule)
    {
        var error = Assert.Single(TestValidators.PasswordChange.Validate(new PasswordChangeInput(current, next, repeat)).Errors);

        Assert.Equal(AccountMessage(rule), error.ErrorMessage);
    }

    [Theory]
    [InlineData(null, "a@example.com", "5551234567", UserRole.User, "required")]
    [InlineData(Password, "a@example.com", "5551234567", (UserRole)9, "required")]
    [InlineData(Password, "eposta", "1", UserRole.User, "email")]
    [InlineData(Password, "a@example.com", "1", UserRole.User, "phone")]
    public void NewAccount_ReportsTheFirstBrokenRule(string? password, string email, string phone, UserRole role, string rule)
    {
        var input = NewAccountInput.Create(new AccountInput("yeni", password, email, phone, role));

        Assert.Equal(AccountMessage(rule), Assert.Single(TestValidators.NewAccount.Validate(input).Errors).ErrorMessage);
    }

    [Fact]
    public void NewAccount_ByAnAdmin_MayUseTheReservedName() =>
        Assert.True(TestValidators.NewAccount.Validate(NewAccountInput.Create(new AccountInput("admin", Password, "a@example.com", "5551234567", UserRole.Admin))).IsValid);

    [Fact]
    public void AccountChange_EmptyContactAndPassword_AreKept()
    {
        var input = AccountChangeInput.Create(new AccountInput("ornek", "", " ", "  ", UserRole.User), keepsStoredName: false);

        Assert.Null(input.Email);
        Assert.Null(input.Phone);
        Assert.True(TestValidators.AccountChange.Validate(input).IsValid);
    }

    [Fact]
    public void AccountChange_StoredNameOutsideTodaysRules_IsKeptButNotANewOne()
    {
        var legacy = new AccountInput("Ali Veli", null, null, null, UserRole.User);

        Assert.True(TestValidators.AccountChange.Validate(AccountChangeInput.Create(legacy, keepsStoredName: true)).IsValid);
        Assert.Equal(AccountRules.UserNameInvalid, Assert.Single(TestValidators.AccountChange.Validate(AccountChangeInput.Create(legacy, keepsStoredName: false)).Errors).ErrorMessage);
    }

    [Fact]
    public void Film_EmptyForm_ReportsEveryFieldInTheOrderOfTheForm()
    {
        var fields = FilmFields.Create(new FilmInput(" ", null, "", "", "", null, "", 0, true), 2026, isChange: false);

        var errors = TestValidators.Film.Validate(fields).Errors;

        Assert.Equal(
            [
                (nameof(FilmInput.Title), "Ad boş bırakılamaz."),
                (nameof(FilmInput.Genre), "Tür boş bırakılamaz."),
                (nameof(FilmInput.Director), "Yönetmen boş bırakılamaz."),
                (nameof(FilmInput.About), "Hakkında kısmı boş bırakılamaz."),
                (nameof(FilmInput.Year), "Yıl boş bırakılamaz."),
                (nameof(FilmInput.DailyPrice), "Ücret boş bırakılamaz."),
                (nameof(FilmInput.Copies), "Adet 1 ile 1000 arasında olmalıdır."),
            ],
            errors.Select(e => (e.PropertyName, e.ErrorMessage)));
    }

    [Theory]
    [InlineData(nameof(FilmInput.Year), "1899", false)]
    [InlineData(nameof(FilmInput.Year), "1900", true)]
    [InlineData(nameof(FilmInput.Year), "2026", true)]
    [InlineData(nameof(FilmInput.Year), "2027", false)]
    [InlineData(nameof(FilmInput.Year), "-2000", false)]
    [InlineData(nameof(FilmInput.DailyPrice), "0", false)]
    [InlineData(nameof(FilmInput.DailyPrice), "0,01", true)]
    [InlineData(nameof(FilmInput.DailyPrice), "12,505", false)]
    [InlineData(nameof(FilmInput.DailyPrice), "99999999,99", true)]
    [InlineData(nameof(FilmInput.DailyPrice), "100000000", false)]
    [InlineData(nameof(FilmInput.DailyPrice), "1.250", false)]
    [InlineData(nameof(FilmInput.ImageUrl), "https://ornek.test/afis.png", true)]
    [InlineData(nameof(FilmInput.ImageUrl), "ftp://ornek.test/afis.png", false)]
    [InlineData(nameof(FilmInput.ImageUrl), "C:\\afis.png", false)]
    [InlineData(nameof(FilmInput.ImageUrl), "afis.png", false)]
    [InlineData(nameof(FilmInput.Copies), "0", false)]
    [InlineData(nameof(FilmInput.Copies), "1", true)]
    [InlineData(nameof(FilmInput.Copies), "1000", true)]
    [InlineData(nameof(FilmInput.Copies), "1001", false)]
    public void Film_FieldBoundaries(string field, string value, bool valid)
    {
        var input = field switch
        {
            nameof(FilmInput.Year) => ValidFilm() with { Year = value },
            nameof(FilmInput.DailyPrice) => ValidFilm() with { DailyPrice = value },
            nameof(FilmInput.ImageUrl) => ValidFilm() with { ImageUrl = value },
            _ => ValidFilm() with { Copies = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
        };

        var result = TestValidators.Film.Validate(FilmFields.Create(input, 2026, isChange: false));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(field, Assert.Single(result.Errors).PropertyName);
        }
    }

    [Fact]
    public void Film_TextLengths_AreTheColumnSizes()
    {
        var atLimit = ValidFilm() with
        {
            Title = new string('a', InputRules.TitleMaxLength),
            Genre = new string('b', InputRules.GenreMaxLength),
            Director = new string('c', InputRules.DirectorMaxLength),
            About = new string('d', InputRules.AboutMaxLength),
            ImageUrl = "https://ornek.test/" + new string('e', InputRules.ImageUrlMaxLength - "https://ornek.test/".Length),
        };
        var beyond = atLimit with
        {
            Title = atLimit.Title + "a",
            Genre = atLimit.Genre + "b",
            Director = atLimit.Director + "c",
            About = atLimit.About + "d",
            ImageUrl = atLimit.ImageUrl + "e",
        };

        Assert.True(TestValidators.Film.Validate(FilmFields.Create(atLimit, 2026, isChange: false)).IsValid);
        var errors = TestValidators.Film.Validate(FilmFields.Create(beyond, 2026, isChange: false)).Errors;
        Assert.Equal(
            ["Ad en fazla 50 karakter olabilir.", "Tür en fazla 50 karakter olabilir.", "Yönetmen en fazla 50 karakter olabilir.", "Hakkında kısmı en fazla 4000 karakter olabilir.", FilmFieldsValidator.ImageUrlInvalid],
            errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void Film_EmptyDescription_IsAllowedOnAChangeOnly()
    {
        var withoutAbout = ValidFilm() with { About = "  " };

        Assert.True(TestValidators.Film.Validate(FilmFields.Create(withoutAbout, 2026, isChange: true)).IsValid);
        Assert.Equal(nameof(FilmInput.About), Assert.Single(TestValidators.Film.Validate(FilmFields.Create(withoutAbout, 2026, isChange: false)).Errors).PropertyName);
    }

    [Theory]
    [InlineData(0, "10", "days")]
    [InlineData(31, "10", "days")]
    [InlineData(1, "0", "price")]
    [InlineData(30, "-1", "price")]
    [InlineData(30, "99999999.99", "total")]
    public void Quote_ReportsDaysThenPriceThenTotal(int days, string price, string rule)
    {
        var policy = new RentalPolicy { MaxRentalDays = 30 };
        var input = new RentalQuoteInput(days, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture));

        var error = Assert.Single(TestValidators.Quote(policy).Validate(input).Errors);

        var expected = rule switch
        {
            "days" => RentalQuoteInputValidator.DaysInvalid(policy),
            "price" => RentalService.FilmNotFound,
            _ => RentalService.TotalTooHigh,
        };
        Assert.Equal(expected, error.ErrorMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void Quote_DaysWithinTheSettings_AreAccepted(int days) =>
        Assert.True(TestValidators.Quote(new RentalPolicy { MaxRentalDays = 30 }).Validate(new RentalQuoteInput(days, 12.5m)).IsValid);

    [Fact]
    public void ValidateOrThrow_CarriesTheFirstMessageOnly()
    {
        IValidator<SignUpInput> validator = TestValidators.SignUp;

        var error = Assert.Throws<BusinessRuleException>(() => validator.ValidateOrThrow(SignUpInput.Create("ab", "kisa", "eposta", "1")));

        Assert.Equal(AccountRules.UserNameInvalid, error.Message);
        Assert.Empty(error.FieldErrors);
    }

    private static FilmInput ValidFilm() =>
        new("Deneme", "Dram", "2020", "Ece Deneme", "12,50", null, "Deneme filmi.", 3, true);

    private static string AccountMessage(string rule) => rule switch
    {
        "required" => AccountRules.AllFieldsRequired,
        "name" => AccountRules.UserNameInvalid,
        "reserved" => AccountRules.UserNameReserved,
        "password" => AccountRules.PasswordInvalid,
        "email" => AccountRules.EmailInvalid,
        "phone" => AccountRules.PhoneInvalid,
        "match" => AccountRules.PasswordsDoNotMatch,
        "new-match" => AuthService.NewPasswordsDoNotMatch,
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };
}
