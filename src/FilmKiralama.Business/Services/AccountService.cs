using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Services;

/// <summary>An account as the admin's account form enters it; an empty password keeps the current one on a change.</summary>
public sealed record AccountInput(string? UserName, string? Password, string? Email, string? Phone, UserRole Role);

/// <summary>The admin's account management. Passwords are never read back, only replaced.</summary>
public interface IAccountService
{
    Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds an account with any role; every field is required.</summary>
    Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>Changes an account; the last admin keeps the admin role.</summary>
    Task UpdateAsync(UserSession session, int userId, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes an account that is no admin and has no rentals.</summary>
    Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default);
}

internal sealed class AccountService(IUserRepository users, IPasswordHasher passwordHasher, IAuditLog audit) : IAccountService
{
    internal const string NotFound = "Kullanıcı bulunamadı.";
    internal const string LastAdmin = "Sistemde en az bir yönetici kalmalıdır; son yöneticinin rolü değiştirilemez.";
    internal const string AdminNotDeletable = "Yönetici rolündeki kullanıcılar silinemez.";
    internal const string HasRentals = "Kiralama geçmişi olan kullanıcı silinemez.";

    public Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        return users.GetAllAsync(cancellationToken);
    }

    public async Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Password))
        {
            throw new BusinessRuleException(AccountRules.AllFieldsRequired);
        }

        var (name, email, phone) = Validate(input);
        AccountRules.RequireValidPassword(input.Password);
        await RequireFreeAsync(name, email, phone, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var id = await users.AddAsync(
                new User
                {
                    UserName = name,
                    PasswordHash = passwordHasher.Hash(input.Password),
                    PasswordFormat = PasswordFormat.Pbkdf2,
                    Role = input.Role,
                    Email = email,
                    Phone = phone,
                },
                cancellationToken).ConfigureAwait(false);
            audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
            return id;
        }
        catch (DuplicateAccountException ex)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }
    }

    public async Task UpdateAsync(UserSession session, int userId, AccountInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        ArgumentNullException.ThrowIfNull(input);

        var (name, email, phone) = Validate(input);
        string? newHash = null;
        if (!string.IsNullOrEmpty(input.Password))
        {
            AccountRules.RequireValidPassword(input.Password);
            newHash = passwordHasher.Hash(input.Password);
        }

        await RequireFreeAsync(name, email, phone, userId, cancellationToken).ConfigureAwait(false);

        AccountUpdateOutcome outcome;
        try
        {
            outcome = await users.UpdateAsync(new AccountUpdate(userId, name, email, phone, input.Role, newHash), cancellationToken).ConfigureAwait(false);
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
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    public async Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var outcome = await users.DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
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

    private static (string Name, string Email, string Phone) Validate(AccountInput input)
    {
        var name = input.UserName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Phone)
            || !Enum.IsDefined(input.Role))
        {
            throw new BusinessRuleException(AccountRules.AllFieldsRequired);
        }

        // Admins may give any name the rules allow, also the one the sign-up form keeps.
        AccountRules.RequireValidUserName(name, allowReserved: true);
        return (name, AccountRules.RequireValidEmail(input.Email), AccountRules.RequireValidPhone(input.Phone));
    }

    private async Task RequireFreeAsync(string name, string email, string phone, int? exceptUserId, CancellationToken cancellationToken)
    {
        var taken = await users.FindTakenAsync(name, email, phone, exceptUserId, cancellationToken).ConfigureAwait(false);
        if (taken != AccountFields.None)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(taken));
        }
    }
}
