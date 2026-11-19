using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Business.Auditing;

/// <summary>
/// Security relevant events. An event carries only its kind and the role of the account involved: no
/// account id, user name, e-mail, phone, password, film or amount, so an audit entry cannot identify a
/// person. The entries can only be counted.
/// </summary>
public enum AuditEvent
{
    /// <summary>A sign-in succeeded.</summary>
    SignInSucceeded = 1,

    /// <summary>
    /// A sign-in failed: unknown account, wrong password, a locked account or an account changed while
    /// it signed in. All cases share one event and are recorded without a role, so the log cannot tell
    /// which accounts exist or are locked.
    /// </summary>
    SignInFailed = 2,

    /// <summary>A plain text or weaker stored password was replaced with a current hash.</summary>
    PasswordUpgraded = 3,

    /// <summary>A new account registered itself.</summary>
    AccountRegistered = 4,

    /// <summary>An account changed its own password.</summary>
    PasswordChanged = 5,

    /// <summary>A password change was refused because the current password was wrong.</summary>
    PasswordChangeRefused = 6,

    /// <summary>The first admin account was created by the first-run setup.</summary>
    FirstAdminCreated = 7,

    /// <summary>A setup key was refused.</summary>
    SetupKeyRefused = 8,

    /// <summary>An account asked for an operation its role does not allow.</summary>
    AccessDenied = 9,

    /// <summary>A film was added, changed or deleted.</summary>
    FilmChanged = 10,

    /// <summary>A copy of a film was rented.</summary>
    RentalCreated = 11,

    /// <summary>A rental was refused because no copy was left.</summary>
    RentalOutOfStock = 12,

    /// <summary>A rented copy came back.</summary>
    RentalReturned = 13,

    /// <summary>An admin added, changed or deleted an account.</summary>
    AccountChanged = 14,
}

/// <summary>Role of the account an <see cref="AuditEvent"/> concerns.</summary>
public enum AuditRole
{
    Unknown = 0,
    User = 1,
    Admin = 2,
}

/// <summary>Receives <see cref="AuditEvent"/>s.</summary>
public interface IAuditLog
{
    void Record(AuditEvent auditEvent, AuditRole role);
}

public static class AuditCodes
{
    /// <summary>Stable, lower case code of <paramref name="auditEvent"/> as it is written to the log.</summary>
    public static string Of(AuditEvent auditEvent) => auditEvent switch
    {
        AuditEvent.SignInSucceeded => "auth.sign_in.succeeded",
        AuditEvent.SignInFailed => "auth.sign_in.failed",
        AuditEvent.PasswordUpgraded => "auth.password.upgraded",
        AuditEvent.AccountRegistered => "auth.account.registered",
        AuditEvent.PasswordChanged => "auth.password.changed",
        AuditEvent.PasswordChangeRefused => "auth.password.change_refused",
        AuditEvent.FirstAdminCreated => "setup.first_admin.created",
        AuditEvent.SetupKeyRefused => "setup.key.refused",
        AuditEvent.AccessDenied => "access.denied",
        AuditEvent.FilmChanged => "film.changed",
        AuditEvent.RentalCreated => "rental.created",
        AuditEvent.RentalOutOfStock => "rental.out_of_stock",
        AuditEvent.RentalReturned => "rental.returned",
        AuditEvent.AccountChanged => "account.changed",
        _ => throw new ArgumentOutOfRangeException(nameof(auditEvent), auditEvent, "Unknown audit event."),
    };

    /// <summary>Lower case role name as it is written to the log.</summary>
    public static string Of(AuditRole role) => role switch
    {
        AuditRole.User => "user",
        AuditRole.Admin => "admin",
        _ => "unknown",
    };

    public static AuditRole Of(UserRole role) => role == UserRole.Admin ? AuditRole.Admin : AuditRole.User;
}

/// <summary>Discards every event; used when no audit sink is registered, for example in tests.</summary>
internal sealed class NullAuditLog : IAuditLog
{
    public static readonly NullAuditLog Instance = new();

    public void Record(AuditEvent auditEvent, AuditRole role)
    {
    }
}
