namespace FilmKiralama.Domain.Entities;

/// <summary>
/// A single use setup key (table KurulumAnahtarlari). Keys are created only by the database owner with
/// docs/issue-setup-key.sql, which shows the key once and stores its SHA-256 hash. The application's
/// login may read the rows and mark a key used (<see cref="Consumed"/>, <see cref="ConsumedAt"/>),
/// nothing else: it can neither create, replace, extend nor delete a key. A key lets the first admin be
/// created on a database that has accounts but no admin, once, before it expires.
/// </summary>
public sealed class SetupKey
{
    public int Id { get; set; }

    /// <summary>SHA-256 of the normalized key (32 upper case hexadecimal characters, ASCII).</summary>
    public byte[] KeyHash { get; set; } = [];

    /// <summary>UTC time of issue.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time after which the key is no longer accepted.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Set in the same transaction as the admin write the key authorized; the issuing script also sets
    /// it to revoke a key.
    /// </summary>
    public bool Consumed { get; set; }

    /// <summary>UTC time the key was used or revoked, or <see langword="null"/> while it is unused.</summary>
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>
/// What a sign-in needs to know about an account; <see cref="SessionVersion"/> is the version a session
/// issued from it carries.
/// </summary>
public sealed record AccountCredentials(
    int Id,
    string UserName,
    UserRole Role,
    string PasswordHash,
    PasswordFormat PasswordFormat,
    int FailedLoginCount,
    DateTime? LockoutEnd,
    int SessionVersion);

/// <summary>
/// A refused sign-in (or a wrong current password) to count against account <see cref="UserName"/>: the
/// password value, its format and the session version are the ones the refused attempt was checked
/// against. The failure is counted only while the stored account still has all three, so an attempt
/// checked against a password that has been replaced since counts nothing.
/// </summary>
public sealed record FailedSignIn(string UserName, string PasswordHash, PasswordFormat PasswordFormat, int SessionVersion)
{
    /// <summary>A failure checked against <paramref name="account"/> as it was read.</summary>
    public static FailedSignIn For(AccountCredentials account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new(account.UserName, account.PasswordHash, account.PasswordFormat, account.SessionVersion);
    }
}

/// <summary>A usable setup key, as a given key is compared with it.</summary>
public sealed record StoredSetupKey(int Id, byte[] KeyHash);

/// <summary>
/// An account as the admin's account list shows it; the password never leaves the database.
/// <see cref="Version"/> is the account's session version when it was read: a change made from a form
/// that loaded it carries it back, and is refused once another admin has changed the account's role or
/// password since.
/// </summary>
public sealed record UserSummary(int Id, string UserName, string? Email, string? Phone, UserRole Role, int Version);
