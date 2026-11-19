using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Services;

public interface IAuthService
{
    /// <summary>Signs an account in with its user name and password.</summary>
    /// <exception cref="BusinessRuleException">Wrong credentials, missing input or a locked account, all with the same message.</exception>
    Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default);

    /// <summary>Registers a new account with the user role. The password is stored as a hash.</summary>
    Task RegisterAsync(string? userName, string? password, string? email, string? phone, CancellationToken cancellationToken = default);

    /// <summary>Changes the signed-in account's own password after checking the current one.</summary>
    Task ChangePasswordAsync(
        UserSession session,
        string? currentPassword,
        string? newPassword,
        string? newPasswordRepeat,
        CancellationToken cancellationToken = default);
}

internal sealed class AuthService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IAuditLog audit) : IAuthService
{
    internal const string InputRequired = "Lütfen kullanıcı adı ve şifrenizi girin.";
    internal const string NewPasswordsDoNotMatch = "Yeni şifreler birbirini tutmuyor. Lütfen aynı şifreyi girin.";
    internal const string CurrentPasswordWrong = "Mevcut şifreniz yanlış.";

    // Every refused sign-in, also a locked account, gets this answer: the lock state is not disclosed
    // before the user has proven who they are; the lockout rule itself is stated for everyone.
    internal static readonly string CredentialsInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kullanıcı adı veya şifre hatalı. {LoginPolicy.MaxFailedAttempts} hatalı denemeden sonra giriş {LoginPolicy.LockoutDuration.TotalMinutes:0} dakika engellenir.");

    public async Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
        {
            throw new BusinessRuleException(InputRequired);
        }

        if (name.Length > InputRules.StoredUserNameMaxLength || !IsAcceptableSignInPassword(password))
        {
            // No account can have such a name or password; refused like a wrong password.
            passwordHasher.SimulateVerification(password.Length > PasswordPolicy.MaxLength ? password[..PasswordPolicy.MaxLength] : password);
            throw Refused();
        }

        var account = await users.FindByUserNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            passwordHasher.SimulateVerification(password);
            throw Refused();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (account.LockoutEnd > now)
        {
            // Same answer and about the same cost as a wrong password; nothing is written, so the lock
            // neither grows nor ends early.
            passwordHasher.SimulateVerification(password);
            throw Refused();
        }

        var result = passwordHasher.Verify(password, account.PasswordHash, account.PasswordFormat);
        if (result == PasswordVerificationResult.Failed)
        {
            await users.RecordFailedLoginAsync(
                account.Id,
                LoginPolicy.MaxFailedAttempts,
                now + LoginPolicy.LockoutDuration,
                cancellationToken).ConfigureAwait(false);
            throw Refused();
        }

        string? newHash = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Plain text passwords were compared trimmed by the original application.
            var legacy = account.PasswordFormat == PasswordFormat.LegacyPlainText;
            newHash = passwordHasher.Hash(legacy ? password.Trim() : password);
        }

        // The session is issued only if the account is still unlocked and unchanged when the success is
        // written; a lock placed by a concurrent failure since the read wins.
        var recorded = await users.RecordSuccessfulLoginAsync(account, now, newHash, cancellationToken).ConfigureAwait(false);
        if (!recorded)
        {
            throw Refused();
        }

        var role = AuditCodes.Of(account.Role);
        if (newHash is not null)
        {
            audit.Record(AuditEvent.PasswordUpgraded, role);
        }

        audit.Record(AuditEvent.SignInSucceeded, role);
        return new UserSession(account.Id, account.UserName, account.Role);
    }

    public async Task RegisterAsync(
        string? userName,
        string? password,
        string? email,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phone))
        {
            throw new BusinessRuleException(AccountRules.AllFieldsRequired);
        }

        AccountRules.RequireValidUserName(name);
        AccountRules.RequireValidPassword(password);
        var mail = AccountRules.RequireValidEmail(email);
        var digits = AccountRules.RequireValidPhone(phone);

        var taken = await users.FindTakenAsync(name, mail, digits, null, cancellationToken).ConfigureAwait(false);
        if (taken != AccountFields.None)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(taken));
        }

        var user = new User
        {
            UserName = name,
            PasswordHash = passwordHasher.Hash(password),
            PasswordFormat = PasswordFormat.Pbkdf2,
            Role = UserRole.User,
            Email = mail,
            Phone = digits,
        };

        try
        {
            await users.AddAsync(user, null, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateAccountException ex)
        {
            // Registered concurrently between the check and the insert.
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }

        audit.Record(AuditEvent.AccountRegistered, AuditRole.User);
    }

    public async Task ChangePasswordAsync(
        UserSession session,
        string? currentPassword,
        string? newPassword,
        string? newPasswordRepeat,
        CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(newPasswordRepeat))
        {
            throw new BusinessRuleException(AccountRules.AllFieldsRequired);
        }

        if (!string.Equals(newPassword, newPasswordRepeat, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(NewPasswordsDoNotMatch);
        }

        AccountRules.RequireValidPassword(newPassword);

        var role = AuditCodes.Of(session.Role);
        var account = await users.FindByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (account is null || account.LockoutEnd > now || currentPassword.Length > PasswordPolicy.MaxLength)
        {
            passwordHasher.SimulateVerification(currentPassword);
            audit.Record(AuditEvent.PasswordChangeRefused, role);
            throw new BusinessRuleException(CurrentPasswordWrong);
        }

        if (passwordHasher.Verify(currentPassword, account.PasswordHash, account.PasswordFormat) == PasswordVerificationResult.Failed)
        {
            // Counted like a failed sign-in: an unattended session cannot be used to guess the password.
            await users.RecordFailedLoginAsync(account.Id, LoginPolicy.MaxFailedAttempts, now + LoginPolicy.LockoutDuration, cancellationToken).ConfigureAwait(false);
            audit.Record(AuditEvent.PasswordChangeRefused, role);
            throw new BusinessRuleException(CurrentPasswordWrong);
        }

        // Only the password that was just verified is replaced.
        if (!await users.ChangePasswordAsync(account, passwordHasher.Hash(newPassword), cancellationToken).ConfigureAwait(false))
        {
            audit.Record(AuditEvent.PasswordChangeRefused, role);
            throw new BusinessRuleException(CurrentPasswordWrong);
        }

        audit.Record(AuditEvent.PasswordChanged, role);
    }

    /// <summary>The one answer and the one audit code of every refused sign-in.</summary>
    private BusinessRuleException Refused()
    {
        audit.Record(AuditEvent.SignInFailed, AuditRole.Unknown);
        return new BusinessRuleException(CredentialsInvalid);
    }

    /// <summary>Sign-in only bounds the input by what an account can hold; the length rules apply to new passwords.</summary>
    private static bool IsAcceptableSignInPassword([NotNullWhen(true)] string? password) =>
        !string.IsNullOrEmpty(password) && password.Length <= PasswordPolicy.MaxLength;
}
