using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>Sign-in, sign-up and password change with strict mocks: refused input never reaches a repository.</summary>
public sealed class AuthServiceTests
{
    private const string UserName = "ornek";
    private const string Password = "Gizli.Sifre1";
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData(UserName, "")]
    [InlineData(UserName, null)]
    [InlineData(null, Password)]
    public async Task SignIn_MissingInput_IsRejectedWithoutTouchingTheRepository(string? userName, string? password)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(userName, password, Ct));

        Assert.Equal(AuthService.InputRequired, error.Message);
        _users.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Unknown account, wrong password, locked account, an overlong password and an account that changed
    /// while it signed in: one message and one audit entry without a role, so neither the screen nor the
    /// log tells them apart.
    /// </summary>
    [Fact]
    public async Task SignIn_EveryRefusal_HasTheSameMessageAndAuditCode()
    {
        var hash = _hasher.Hash(Password);
        var messages = new List<string>();

        // Unknown account.
        _users.Setup(u => u.FindByUserNameAsync("kimse", It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync("kimse", Password, Ct))).Message);

        // Wrong password.
        Setup(Account(hash));
        _users.Setup(u => u.RecordFailedLoginAsync(7, LoginPolicy.MaxFailedAttempts, Now.AddMinutes(15), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Yanlis.Sifre1", Ct))).Message);

        // Locked account, even with the right password.
        Setup(Account(hash, lockoutEnd: Now.AddMinutes(5)));
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, Password, Ct))).Message);

        // A password no account can have.
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, new string('x', PasswordPolicy.MaxLength + 1), Ct))).Message);

        // The account was locked by a concurrent failure after it was read.
        Setup(Account(hash));
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, Password, Ct))).Message);

        Assert.All(messages, m => Assert.Equal(AuthService.CredentialsInvalid, m));
        Assert.Equal(5, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal("auth.sign_in.failed unknown", e));
    }

    [Fact]
    public async Task SignIn_WrongPassword_CountsTheFailureWithAFifteenMinuteLock()
    {
        Setup(Account(_hasher.Hash(Password)));
        _users.Setup(u => u.RecordFailedLoginAsync(7, 5, Now.AddMinutes(15), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Yanlis.Sifre1", Ct));

        _users.Verify(u => u.RecordFailedLoginAsync(7, 5, Now.AddMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignIn_LockedAccount_WritesNothing()
    {
        Setup(Account(_hasher.Hash(Password), lockoutEnd: Now.AddSeconds(1)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, Password, Ct));

        _users.Verify(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignIn_AfterTheLockEnds_Succeeds()
    {
        Setup(Account(_hasher.Hash(Password), lockoutEnd: Now));
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.Is<AccountCredentials>(c => c.Id == 7), Now, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var session = await Service().SignInAsync(" " + UserName + " ", Password, Ct);

        Assert.Equal(7, session.UserId);
        Assert.Equal(UserRole.User, session.Role);
        Assert.Equal(["auth.sign_in.succeeded user"], _audit.Entries);
    }

    [Fact]
    public async Task SignIn_LegacyPlainText_IsRehashedTrimmedInTheConditionalSuccessWrite()
    {
        Setup(Account("1234", format: PasswordFormat.LegacyPlainText, role: UserRole.Admin));
        string? stored = null;
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.Is<AccountCredentials>(c => c.Id == 7), Now, It.IsNotNull<string>(), It.IsAny<CancellationToken>()))
            .Callback<AccountCredentials, DateTime, string?, CancellationToken>((_, _, hash, _) => stored = hash)
            .ReturnsAsync(true);

        var session = await Service().SignInAsync(UserName, " 1234 ", Ct);

        Assert.True(session.IsAdmin);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("1234", stored!, PasswordFormat.Pbkdf2));
        Assert.Equal(["auth.password.upgraded admin", "auth.sign_in.succeeded admin"], _audit.Entries);
    }

    [Theory]
    [InlineData(null, Password, "a@example.com", "5551234567")]
    [InlineData("yeni", " ", "a@example.com", "5551234567")]
    [InlineData("yeni", Password, "", "5551234567")]
    [InlineData("yeni", Password, "a@example.com", null)]
    public async Task Register_MissingField_IsRejectedWithoutTouchingTheRepository(string? name, string? password, string? email, string? phone)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(name, password, email, phone, Ct));

        Assert.Equal(AccountRules.AllFieldsRequired, error.Message);
        _users.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("admin", Password, "a@example.com", "5551234567")]
    [InlineData("ADMIN", Password, "a@example.com", "5551234567")]
    [InlineData("ab", Password, "a@example.com", "5551234567")]
    [InlineData("iki kelime", Password, "a@example.com", "5551234567")]
    [InlineData("yeni", "kisa", "a@example.com", "5551234567")]
    [InlineData("yeni", Password, "eposta", "5551234567")]
    [InlineData("yeni", Password, "a@example.com", "555123456")]
    [InlineData("yeni", Password, "a@example.com", "55512345678")]
    public async Task Register_InvalidValue_IsRejectedWithoutTouchingTheRepository(string name, string password, string email, string phone)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync(name, password, email, phone, Ct));

        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_TakenValues_ListsEachOfThemAsTheOriginalFormDid()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "a@example.com", "5551234567", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountFields.Email | AccountFields.Phone);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("yeni", Password, "a@example.com", "555 123 4567", Ct));

        Assert.DoesNotContain("kullanıcı adı", error.Message, StringComparison.Ordinal);
        Assert.Contains("e-posta", error.Message, StringComparison.Ordinal);
        Assert.Contains("telefon", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_StoresAUserWithAHashAndTheDigitsOfThePhone()
    {
        User? stored = null;
        _users.Setup(u => u.FindTakenAsync("yeni", "a@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>()))
            .Callback<User, int?, CancellationToken>((user, _, _) => stored = user)
            .ReturnsAsync(9);

        await Service().RegisterAsync(" yeni ", Password, " a@example.com ", "555 123 4567", Ct);

        Assert.NotNull(stored);
        Assert.Equal("yeni", stored.UserName);
        Assert.Equal(UserRole.User, stored.Role);
        Assert.Equal(PasswordFormat.Pbkdf2, stored.PasswordFormat);
        Assert.Equal("5551234567", stored.Phone);
        Assert.Equal("a@example.com", stored.Email);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify(Password, stored.PasswordHash, PasswordFormat.Pbkdf2));
        Assert.Equal(["auth.account.registered user"], _audit.Entries);
    }

    [Fact]
    public async Task Register_ConcurrentDuplicate_BecomesTheSameRuleMessage()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "a@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateAccountException(AccountFields.UserName));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("yeni", Password, "a@example.com", "5551234567", Ct));

        Assert.Contains("kullanıcı adı zaten mevcut", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_CountsAFailureAndChangesNothing()
    {
        var account = Account(_hasher.Hash(Password));
        _users.Setup(u => u.FindByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _users.Setup(u => u.RecordFailedLoginAsync(7, 5, Now.AddMinutes(15), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User), "Yanlis.Sifre1", "Yeni.Sifre12", "Yeni.Sifre12", Ct));

        Assert.Equal(AuthService.CurrentPasswordWrong, error.Message);
        _users.Verify(u => u.ChangePasswordAsync(It.IsAny<AccountCredentials>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(["auth.password.change_refused user"], _audit.Entries);
    }

    [Fact]
    public async Task ChangePassword_ReplacesOnlyTheVerifiedPassword()
    {
        var account = Account(_hasher.Hash(Password));
        string? stored = null;
        _users.Setup(u => u.FindByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _users.Setup(u => u.ChangePasswordAsync(account, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<AccountCredentials, string, CancellationToken>((_, hash, _) => stored = hash)
            .ReturnsAsync(true);

        await Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User), Password, "Yeni.Sifre12", "Yeni.Sifre12", Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", stored!, PasswordFormat.Pbkdf2));
    }

    [Theory]
    [InlineData(Password, "Yeni.Sifre12", "Yeni.Sifre13", AuthService.NewPasswordsDoNotMatch)]
    [InlineData(Password, "kisa", "kisa", null)]
    [InlineData("", "Yeni.Sifre12", "Yeni.Sifre12", AccountRules.AllFieldsRequired)]
    public async Task ChangePassword_InvalidInput_IsRejectedWithoutTouchingTheRepository(string current, string next, string repeat, string? message)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User), current, next, repeat, Ct));

        Assert.Equal(message ?? AccountRules.PasswordInvalid, error.Message);
        _users.VerifyNoOtherCalls();
    }

    private AuthService Service() =>
        new(_users.Object, _hasher, _time, _audit, TestValidators.SignIn, TestValidators.SignUp, TestValidators.PasswordChange);

    private void Setup(AccountCredentials account) =>
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(account);

    private static AccountCredentials Account(
        string hash,
        PasswordFormat format = PasswordFormat.Pbkdf2,
        DateTime? lockoutEnd = null,
        UserRole role = UserRole.User) =>
        new(7, UserName, role, hash, format, 0, lockoutEnd);
}
