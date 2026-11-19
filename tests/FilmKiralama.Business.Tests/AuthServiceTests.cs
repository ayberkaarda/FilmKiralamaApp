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
        Assert.Equal(["auth.sign_in.failed unknown"], _audit.Entries);
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

        _users.Setup(u => u.RecordFailedLoginAsync(It.IsAny<FailedSignIn>(), LoginPolicy.MaxFailedAttempts, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Unknown account.
        _users.Setup(u => u.FindByUserNameAsync("kimse", It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);
        messages.Add((await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync("kimse", Password, Ct))).Message);

        // Wrong password.
        Setup(Account(hash));
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
        _users.Setup(u => u.RecordFailedLoginAsync(It.Is<FailedSignIn>(f => f.UserName == UserName && f.SessionVersion == 0), 5, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, "Yanlis.Sifre1", Ct));

        _users.Verify(u => u.RecordFailedLoginAsync(It.Is<FailedSignIn>(f => f.UserName == UserName && f.SessionVersion == 0), 5, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A locked account is refused before its password is checked, and only the conditional failure write
    /// follows (the same one every refusal makes); while the lock is in force it matches no row, so the
    /// lock neither grows nor ends early. Nothing records a success.
    /// </summary>
    [Fact]
    public async Task SignIn_LockedAccount_WritesOnlyTheConditionalFailure()
    {
        var account = Account(_hasher.Hash(Password), lockoutEnd: Now.AddSeconds(1));
        Setup(account);
        _users.Setup(u => u.RecordFailedLoginAsync(FailedSignIn.For(account), 5, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SignInAsync(UserName, Password, Ct));

        _users.Verify(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(u => u.RecordFailedLoginAsync(FailedSignIn.For(account), 5, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>()), Times.Once);
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
            .Callback<User, Actor?, CancellationToken>((user, _, _) => stored = user)
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
        _users.Setup(u => u.RecordFailedLoginAsync(It.Is<FailedSignIn>(f => f.UserName == UserName && f.SessionVersion == 0), 5, TimeSpan.FromMinutes(15), Now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User, sessionVersion: 0), "Yanlis.Sifre1", "Yeni.Sifre12", "Yeni.Sifre12", Ct));

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
            .ReturnsAsync(1);
        var session = new UserSession(7, UserName, UserRole.User, sessionVersion: 0);

        await Service().ChangePasswordAsync(session, Password, "Yeni.Sifre12", "Yeni.Sifre12", Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", stored!, PasswordFormat.Pbkdf2));
        Assert.Equal(1, session.SessionVersion);
    }

    /// <summary>
    /// A session whose account got a new password or role elsewhere cannot change the password either,
    /// not even with the right current one; it is told to sign in again and nothing is counted or written.
    /// </summary>
    [Fact]
    public async Task ChangePassword_RevokedSession_IsRefusedWithoutCheckingThePassword()
    {
        _users.Setup(u => u.FindByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Account(_hasher.Hash(Password)) with { SessionVersion = 3 });

        var error = await Assert.ThrowsAsync<AccessDeniedException>(
            () => Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User, sessionVersion: 2), Password, "Yeni.Sifre12", "Yeni.Sifre12", Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        _users.Verify(u => u.FindByIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
        Assert.Equal(["access.session_revoked user"], _audit.Entries);
    }

    [Theory]
    [InlineData(Password, "Yeni.Sifre12", "Yeni.Sifre13", AuthService.NewPasswordsDoNotMatch)]
    [InlineData(Password, "kisa", "kisa", null)]
    [InlineData("", "Yeni.Sifre12", "Yeni.Sifre12", AccountRules.AllFieldsRequired)]
    public async Task ChangePassword_InvalidInput_IsRejectedWithoutTouchingTheRepository(string current, string next, string repeat, string? message)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().ChangePasswordAsync(new UserSession(7, UserName, UserRole.User, sessionVersion: 0), current, next, repeat, Ct));

        Assert.Equal(message ?? AccountRules.PasswordInvalid, error.Message);
        _users.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A lock set by another computer ends at the database's time: the shared clock decides, and a
    /// computer whose own clock is a day ahead has no say (the service has no other clock).
    /// </summary>
    [Fact]
    public async Task SignIn_LockedAccount_EndsByTheSharedClockOnly()
    {
        var shared = new FakeTimeProvider(new DateTimeOffset(Now));
        var hash = _hasher.Hash(Password);
        var locked = new AccountCredentials(4, UserName, UserRole.User, hash, PasswordFormat.Pbkdf2, 0, Now.AddMinutes(15), SessionVersion: 0);
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(locked);
        _users.Setup(u => u.RecordSuccessfulLoginAsync(locked, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.RecordFailedLoginAsync(FailedSignIn.For(locked), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new AuthService(_users.Object, _hasher, new TimeProviderClock(shared), _audit, TestValidators.SignIn, TestValidators.SignUp, TestValidators.PasswordChange);

        _time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SignInAsync(UserName, Password, Ct));

        shared.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(4, (await service.SignInAsync(UserName, Password, Ct)).UserId);
        _users.Verify(u => u.RecordSuccessfulLoginAsync(locked, Now.AddMinutes(15), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Every sign-in that reaches the database makes the same calls in the same order, whatever the
    /// outcome: the database's clock, the lookup by name, one password check (real or simulated) and one
    /// write of the outcome. An unknown name, a wrong password and a locked account all end with the same
    /// failure write (which matches no row, or counts, as the database decides), so the number of round
    /// trips does not tell an existing account from a missing one.
    /// </summary>
    [Fact]
    public async Task SignIn_EveryOutcome_MakesTheSameCallsInTheSameOrder()
    {
        var steps = new List<string>();
        var failures = new List<FailedSignIn>();
        var clock = new Mock<IClock>(MockBehavior.Strict);
        clock.Setup(c => c.GetUtcNowAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("clock")).ReturnsAsync(Now);
        var hasher = new Mock<IPasswordHasher>(MockBehavior.Strict);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PasswordFormat>()))
            .Callback(() => steps.Add("password"))
            .Returns<string, string, PasswordFormat>((password, stored, format) => _hasher.Verify(password, stored, format));
        hasher.Setup(h => h.SimulateVerification(It.IsAny<string>())).Callback(() => steps.Add("password"));
        var hash = _hasher.Hash(Password);
        var locked = new AccountCredentials(8, "kilitli", UserRole.User, hash, PasswordFormat.Pbkdf2, 0, Now.AddMinutes(5), SessionVersion: 2);
        _users.Setup(u => u.FindByUserNameAsync("kimse", It.IsAny<CancellationToken>())).Callback(() => steps.Add("lookup")).ReturnsAsync((AccountCredentials?)null);
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).Callback(() => steps.Add("lookup")).ReturnsAsync(Account(hash));
        _users.Setup(u => u.FindByUserNameAsync("kilitli", It.IsAny<CancellationToken>())).Callback(() => steps.Add("lookup")).ReturnsAsync(locked);
        _users.Setup(u => u.RecordFailedLoginAsync(It.IsAny<FailedSignIn>(), LoginPolicy.MaxFailedAttempts, LoginPolicy.LockoutDuration, Now, It.IsAny<CancellationToken>()))
            .Callback<FailedSignIn, int, TimeSpan, DateTime, CancellationToken>((failure, _, _, _, _) => { steps.Add("record failure"); failures.Add(failure); })
            .Returns(Task.CompletedTask);
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, null, It.IsAny<CancellationToken>()))
            .Callback(() => steps.Add("record success"))
            .ReturnsAsync(true);
        var service = new AuthService(_users.Object, hasher.Object, clock.Object, _audit, TestValidators.SignIn, TestValidators.SignUp, TestValidators.PasswordChange);

        async Task<List<string>> RunAsync(string name, string password, bool succeeds)
        {
            steps.Clear();
            if (succeeds)
            {
                await service.SignInAsync(name, password, Ct);
            }
            else
            {
                await Assert.ThrowsAsync<BusinessRuleException>(() => service.SignInAsync(name, password, Ct));
            }

            return [.. steps];
        }

        var unknown = await RunAsync("kimse", Password, succeeds: false);
        var wrong = await RunAsync(UserName, "Yanlis.Sifre1", succeeds: false);
        var whileLocked = await RunAsync("kilitli", Password, succeeds: false);
        var success = await RunAsync(UserName, Password, succeeds: true);

        List<string> refused = ["clock", "lookup", "password", "record failure"];
        Assert.Equal(refused, unknown);
        Assert.Equal(refused, wrong);
        Assert.Equal(refused, whileLocked);
        Assert.Equal(["clock", "lookup", "password", "record success"], success);

        // The unknown name's write can match no row; the others carry the credential that was checked.
        Assert.Equal(("kimse", -1), (failures[0].UserName, failures[0].SessionVersion));
        Assert.Equal((UserName, hash, 0), (failures[1].UserName, failures[1].PasswordHash, failures[1].SessionVersion));
        Assert.Equal(("kilitli", 2), (failures[2].UserName, failures[2].SessionVersion));
    }

    /// <summary>
    /// Every type that carries a session version needs it explicitly: a call site that forgot it would
    /// otherwise pass 0, which every account whose role and password never changed accepts.
    /// </summary>
    [Theory]
    [InlineData(typeof(UserSession))]
    [InlineData(typeof(Actor))]
    [InlineData(typeof(RentalActor))]
    [InlineData(typeof(RentalRequest))]
    [InlineData(typeof(AccountCredentials))]
    [InlineData(typeof(FailedSignIn))]
    public void SessionVersion_HasNoDefaultValue(Type type)
    {
        var parameters = type.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .SelectMany(c => c.GetParameters())
            .Where(p => string.Equals(p.Name, "sessionVersion", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.All(parameters, p => Assert.False(p.HasDefaultValue, $"{type.Name}({p.Name}) has a default value."));
    }

    private AuthService Service() =>
        new(_users.Object, _hasher, new TimeProviderClock(_time), _audit, TestValidators.SignIn, TestValidators.SignUp, TestValidators.PasswordChange);

    private void Setup(AccountCredentials account) =>
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>())).ReturnsAsync(account);

    private static AccountCredentials Account(
        string hash,
        PasswordFormat format = PasswordFormat.Pbkdf2,
        DateTime? lockoutEnd = null,
        UserRole role = UserRole.User) =>
        new(7, UserName, role, hash, format, 0, lockoutEnd, SessionVersion: 0);
}
