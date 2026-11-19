using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using FluentValidation;

namespace FilmKiralama.Business.Services;

/// <summary>An account as the admin's account form enters it; an empty password keeps the current one on a change.</summary>
public sealed record AccountInput(string? UserName, string? Password, string? Email, string? Phone, UserRole Role);

/// <summary>The admin's account management. Passwords are never read back, only replaced.</summary>
public interface IAccountService
{
    Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds an account with any role; every field is required.</summary>
    Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an account loaded at <paramref name="expectedVersion"/> (<see cref="UserSummary.Version"/>);
    /// the last admin keeps the admin role. E-mail and phone may be empty (accounts kept from the original
    /// application have none) and are checked only when given.
    /// </summary>
    /// <exception cref="BusinessRuleException">
    /// The input is invalid, or (with <see cref="BusinessRuleException.IsStale"/>) another admin changed the
    /// account's role or password, or deleted it, since it was loaded.
    /// </exception>
    Task UpdateAsync(UserSession session, int userId, int expectedVersion, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes an account that is no admin and has no rentals.</summary>
    Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default);
}

internal sealed class AccountService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuditLog audit,
    IValidator<NewAccountInput> newAccountValidator,
    IValidator<AccountChangeInput> accountChangeValidator) : IAccountService
{
    internal const string NotFound = "Kullanıcı bulunamadı.";
    internal const string LastAdmin = "Sistemde en az bir yönetici kalmalıdır; son yöneticinin rolü değiştirilemez.";
    internal const string AdminNotDeletable = "Yönetici rolündeki kullanıcılar silinemez.";
    internal const string HasRentals = "Kiralama geçmişi olan kullanıcı silinemez.";
    internal const string ChangedElsewhere = "Kullanıcının rolü veya şifresi siz düzenlerken başka biri tarafından değiştirildi. Güncel hali yüklendi; lütfen değişikliğinizi yeniden yapın.";

    public Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        return audit.AsActorAsync(session, () => users.GetAllAsync(session.Actor, cancellationToken));
    }

    public async Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);

        // Admins may give any name the rules allow, also the one the sign-up form keeps.
        var account = NewAccountInput.Create(input);
        newAccountValidator.ValidateOrThrow(account);
        var (name, email, phone) = (account.UserName, account.Email, InputRules.NormalizePhone(account.Phone)!);
        await RequireFreeAsync(name, email, phone, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var user = new User
            {
                UserName = name,
                PasswordHash = passwordHasher.Hash(account.Password!),
                PasswordFormat = PasswordFormat.Pbkdf2,
                Role = input.Role,
                Email = email,
                Phone = phone,
            };
            var id = await audit.AsActorAsync(session, () => users.AddAsync(user, session.Actor, cancellationToken)).ConfigureAwait(false);
            audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
            return id;
        }
        catch (DuplicateAccountException ex)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }
    }

    public async Task UpdateAsync(UserSession session, int userId, int expectedVersion, AccountInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        ArgumentNullException.ThrowIfNull(input);

        var change = AccountChangeInput.Create(input, await KeepsStoredNameAsync(userId, input, cancellationToken).ConfigureAwait(false));
        accountChangeValidator.ValidateOrThrow(change);
        var (name, email, phone) = (change.UserName, change.Email, change.Phone is null ? null : InputRules.NormalizePhone(change.Phone));
        var newHash = string.IsNullOrEmpty(change.Password) ? null : passwordHasher.Hash(change.Password);

        await RequireFreeAsync(name, email, phone, userId, cancellationToken).ConfigureAwait(false);

        AccountUpdateOutcome outcome;
        try
        {
            var update = new AccountUpdate(userId, expectedVersion, name, email, phone, input.Role, newHash);
            outcome = await audit.AsActorAsync(session, () => users.UpdateAsync(session.Actor, update, cancellationToken)).ConfigureAwait(false);
        }
        catch (DuplicateAccountException ex)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }

        switch (outcome)
        {
            case AccountUpdateOutcome.Updated:
                audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
                return;
            case AccountUpdateOutcome.LastAdmin:
                throw new BusinessRuleException(LastAdmin);
            case AccountUpdateOutcome.ChangedElsewhere:
                throw BusinessRuleException.Stale(ChangedElsewhere);
            default:
                throw BusinessRuleException.Stale(NotFound);
        }
    }

    public async Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var outcome = await audit.AsActorAsync(session, () => users.DeleteAsync(session.Actor, userId, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case AccountDeleteOutcome.Deleted:
                audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
                return;
            case AccountDeleteOutcome.IsAdmin:
                throw new BusinessRuleException(AdminNotDeletable);
            case AccountDeleteOutcome.HasRentals:
                throw new BusinessRuleException(HasRentals);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    /// <summary>
    /// Whether the changed account keeps a name today's rules do not allow because it is its stored name
    /// and fits its column, so an original account can be changed without being renamed. The account is
    /// read only for such a name. Unlike a new account, e-mail and phone may stay empty on a change (they
    /// are stored as NULL, as the upgrade keeps them for accounts of the original application).
    /// </summary>
    private async Task<bool> KeepsStoredNameAsync(int userId, AccountInput input, CancellationToken cancellationToken)
    {
        var name = input.UserName?.Trim();
        if (string.IsNullOrEmpty(name) || !Enum.IsDefined(input.Role) || InputRules.IsValidUserName(name))
        {
            return false;
        }

        var stored = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return stored is not null
            && string.Equals(stored.UserName, name, StringComparison.Ordinal)
            && name.Length <= InputRules.StoredUserNameMaxLength;
    }

    private async Task RequireFreeAsync(string name, string? email, string? phone, int? exceptUserId, CancellationToken cancellationToken)
    {
        var taken = await users.FindTakenAsync(name, email, phone, exceptUserId, cancellationToken).ConfigureAwait(false);
        if (taken != AccountFields.None)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(taken));
        }
    }
}
