using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>
/// The audit codes: fixed, lower case and free of identities. Outcomes that would tell whether an
/// account or a rental exists share one entry.
/// </summary>
public sealed class AuditTests
{
    private const string Password = "Gizli.Sifre1";
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Codes_AreFixed()
    {
        Assert.Equal(
            [
                "auth.sign_in.succeeded", "auth.sign_in.failed", "auth.password.upgraded", "auth.account.registered",
                "auth.password.changed", "auth.password.change_refused", "setup.first_admin.created", "setup.key.refused",
                "access.denied", "film.changed", "rental.created", "rental.out_of_stock", "rental.returned",
                "account.changed", "access.session_revoked", "auth.account.registration_not_completed",
                "review.saved", "review.deleted", "review.moderated", "watchlist.changed", "report.exported",
            ],
            Enum.GetValues<AuditEvent>().Select(AuditCodes.Of));
        Assert.Equal(["unknown", "user", "admin"], Enum.GetValues<AuditRole>().Select(AuditCodes.Of));
    }

    /// <summary>Only the two sign-up outcomes are merged in the log file; every other event keeps its code.</summary>
    [Fact]
    public void PublicLog_MergesTheSignUpOutcomesOnly()
    {
        Assert.Equal(AuditCodes.RegistrationSubmitted, AuditCodes.ForPublicLog(AuditEvent.AccountRegistered));
        Assert.Equal(AuditCodes.RegistrationSubmitted, AuditCodes.ForPublicLog(AuditEvent.RegistrationNotCompleted));
        Assert.All(
            Enum.GetValues<AuditEvent>().Except([AuditEvent.AccountRegistered, AuditEvent.RegistrationNotCompleted]),
            e => Assert.Equal(AuditCodes.Of(e), AuditCodes.ForPublicLog(e)));
        Assert.Equal(Enum.GetValues<AuditEvent>().Length - 1, Enum.GetValues<AuditEvent>().Select(AuditCodes.ForPublicLog).Distinct().Count());
    }

    /// <summary>
    /// Every refused sign-in, from an empty field to an account locked by a concurrent failure, leaves
    /// the same code without a role.
    /// </summary>
    [Fact]
    public async Task SignIn_EveryRefusal_RecordsTheSameRolelessCode()
    {
        var hash = _hasher.Hash(Password);
        _users.Setup(u => u.FindByUserNameAsync("kimse", It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);
        _users.Setup(u => u.FindByUserNameAsync("kilitli", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(8, "kilitli", UserRole.Admin, hash, PasswordFormat.Pbkdf2, 5, Now.AddMinutes(10), SessionVersion: 0));
        _users.Setup(u => u.FindByUserNameAsync("ornek", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(7, "ornek", UserRole.Admin, hash, PasswordFormat.Pbkdf2, 0, null, SessionVersion: 0));
        _users.Setup(u => u.RecordFailedLoginAsync(7, It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        List<Func<Task>> refusals =
        [
            () => Service().SignInAsync("", Password, Ct),
            () => Service().SignInAsync("ornek", null, Ct),
            () => Service().SignInAsync(new string('a', 51), Password, Ct),
            () => Service().SignInAsync("ornek", new string('x', PasswordPolicy.MaxLength + 1), Ct),
            () => Service().SignInAsync("kimse", Password, Ct),
            () => Service().SignInAsync("kilitli", Password, Ct),
            () => Service().SignInAsync("ornek", "Yanlis.Sifre1", Ct),
        ];
        foreach (var refusal in refusals)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(refusal);
        }

        Assert.Equal(refusals.Count, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal("auth.sign_in.failed unknown", e));
    }

    /// <summary>A new account and a sign-up with taken values look the same in the log file.</summary>
    [Fact]
    public async Task SignUp_NewAndTaken_LeaveTheSamePublicCode()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _users.Setup(u => u.FindTakenAsync("alinmis", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.UserName);
        _users.Setup(u => u.FindTakenAsync("yaris", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        var publicLog = new PublicLog();
        var service = Service(publicLog);

        await service.RegisterAsync("yeni", Password, "yeni@example.com", "5551234567", Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync("alinmis", Password, "yeni@example.com", "5551234567", Ct));
        _users.Setup(u => u.AddAsync(It.Is<User>(x => x.UserName == "yaris"), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateAccountException(AccountFields.Email));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync("yaris", Password, "yeni@example.com", "5551234567", Ct));

        Assert.Equal(["auth.account.registration_submitted user", "auth.account.registration_submitted user", "auth.account.registration_submitted user"], publicLog.Entries);
    }

    private AuthService Service(IAuditLog? audit = null) => new(
        _users.Object,
        _hasher,
        new TimeProviderClock(new FakeTimeProvider(new DateTimeOffset(Now))),
        audit ?? _audit,
        TestValidators.SignIn,
        TestValidators.SignUp,
        TestValidators.PasswordChange);

    /// <summary>Collects events as the application's log file writes them.</summary>
    private sealed class PublicLog : IAuditLog
    {
        public List<string> Entries { get; } = [];

        public void Record(AuditEvent auditEvent, AuditRole role) =>
            Entries.Add(AuditCodes.ForPublicLog(auditEvent) + " " + AuditCodes.Of(role));
    }
}
