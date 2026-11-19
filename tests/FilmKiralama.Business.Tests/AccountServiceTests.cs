using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Moq;

namespace FilmKiralama.Business.Tests;

public sealed class AccountServiceTests
{
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AccountInput Valid => new("yeni", "Gizli.Sifre1", "yeni@example.com", "555 000 0009", UserRole.User);

    [Fact]
    public async Task EveryOperation_ForAUser_IsDeniedBeforeAnyRepositoryCall()
    {
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.GetAllAsync(Sessions.User, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.User, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.User, 2, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.User, 5, Ct));

        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Add_WithoutPassword_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Password = "" }, Ct));

        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Update_WithoutPassword_KeepsTheStoredOne()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5550000009", 4, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, new AccountUpdate(4, "yeni", "yeni@example.com", "5550000009", UserRole.Admin, null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 4, Valid with { Password = "", Role = UserRole.Admin }, Ct);

        Assert.Equal(["account.changed admin"], _audit.Entries);
    }

    [Fact]
    public async Task Update_WithPassword_StoresAHash()
    {
        string? hash = null;
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5550000009", 4, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<int, AccountUpdate, CancellationToken>((_, update, _) => hash = update.NewPasswordHash)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 4, Valid, Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Gizli.Sifre1", hash!, Domain.Entities.PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task Update_LastAdmin_KeepsTheRole()
    {
        _users.Setup(u => u.FindTakenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), 1, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>())).ReturnsAsync(AccountUpdateOutcome.LastAdmin);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 1, Valid with { Password = null }, Ct));

        Assert.Equal(AccountService.LastAdmin, error.Message);
    }

    [Theory]
    [InlineData(AccountDeleteOutcome.IsAdmin, AccountService.AdminNotDeletable)]
    [InlineData(AccountDeleteOutcome.HasRentals, AccountService.HasRentals)]
    [InlineData(AccountDeleteOutcome.NotFound, AccountService.NotFound)]
    public async Task Delete_RefusedByTheRepository_BecomesARuleMessage(AccountDeleteOutcome outcome, string message)
    {
        _users.Setup(u => u.DeleteAsync(Sessions.Admin.UserId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteAsync(Sessions.Admin, 5, Ct));

        Assert.Equal(message, error.Message);
    }

    /// <summary>The upgrade keeps original accounts without e-mail and phone; an admin can still reset their password.</summary>
    [Fact]
    public async Task Update_LegacyAccountWithoutEmailOrPhone_ResetsPassword()
    {
        AccountUpdate? sent = null;
        _users.Setup(u => u.FindTakenAsync("eski", null, null, 6, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<int, AccountUpdate, CancellationToken>((_, update, _) => sent = update)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski", "Yeni.Sifre12", " ", "", UserRole.User), Ct);

        Assert.NotNull(sent);
        Assert.Null(sent.Email);
        Assert.Null(sent.Phone);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", sent.NewPasswordHash!, Domain.Entities.PasswordFormat.Pbkdf2));
    }

    /// <summary>An original account keeps a name today's rules no longer allow, as long as it stays the same.</summary>
    [Fact]
    public async Task Update_LegacyNameKeptAsItIs_IsAcceptedButNotANewOneLikeIt()
    {
        // Spaces and 40 characters: refused for new names, kept for the original account.
        var legacyName = "eski hesap adi bosluklu ve 32den uzun ad";
        _users.Setup(u => u.FindByIdAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(6, legacyName, UserRole.User, "abcd", Domain.Entities.PasswordFormat.LegacyPlainText, 0, null));
        _users.Setup(u => u.FindTakenAsync(legacyName, null, null, 6, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.Is<AccountUpdate>(a => a.UserName == legacyName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 6, new AccountInput(legacyName, "Yeni.Sifre12", null, null, UserRole.User), Ct);
        var renamed = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("baska bosluklu ad", null, null, null, UserRole.User), Ct));

        Assert.Equal(AccountRules.UserNameInvalid, renamed.Message);
    }

    [Fact]
    public async Task Update_ContactValuesThatAreGiven_AreStillChecked()
    {
        var email = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 6, Valid with { Email = "eposta-degil" }, Ct));
        var phone = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 6, Valid with { Phone = "12" }, Ct));
        var addition = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Email = null }, Ct));

        Assert.Equal(AccountRules.EmailInvalid, email.Message);
        Assert.Equal(AccountRules.PhoneInvalid, phone.Message);
        Assert.Equal(AccountRules.AllFieldsRequired, addition.Message);
        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Update_ByAnAdminWhoseStoredRoleIsGone_AsksToSignInAgain()
    {
        _users.Setup(u => u.FindTakenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), 1, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());
        _users.Setup(u => u.DeleteAsync(Sessions.Admin.UserId, 5, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());

        var update = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().UpdateAsync(Sessions.Admin, 1, Valid with { Role = UserRole.Admin }, Ct));
        var delete = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().DeleteAsync(Sessions.Admin, 5, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, update.Message);
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, delete.Message);
        Assert.Equal(["access.session_revoked admin", "access.session_revoked admin"], _audit.Entries);
    }

    [Fact]
    public async Task Add_UnknownRole_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Role = (UserRole)7 }, Ct));

        _users.VerifyNoOtherCalls();
    }

    private AccountService Service() => new(_users.Object, _hasher, _audit, TestValidators.NewAccount, TestValidators.AccountChange);
}
