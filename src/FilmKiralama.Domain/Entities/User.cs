namespace FilmKiralama.Domain.Entities;

/// <summary>An account that can sign in (table Kullanicilar).</summary>
public sealed class User
{
    public int Id { get; set; }

    /// <summary>Sign-in name; unique.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash of the password. Rows created by the original application hold the plain text
    /// password until the first successful sign-in replaces it with a hash; <see cref="PasswordFormat"/>
    /// tells which of the two it is.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>How <see cref="PasswordHash"/> is stored.</summary>
    public PasswordFormat PasswordFormat { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>E-mail address; unique when set. The original application allowed it to be empty.</summary>
    public string? Email { get; set; }

    /// <summary>Phone number as ten digits; unique when set.</summary>
    public string? Phone { get; set; }

    /// <summary>Failed sign-in attempts since the last success or lockout.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>UTC time until which sign-in is refused, or <see langword="null"/>.</summary>
    public DateTime? LockoutEnd { get; set; }

    /// <summary>
    /// Raised by every change that has to end the account's open sessions: a new password (its own change
    /// or one set by an admin) and a new role. A session carries the version it was issued with, and every
    /// operation compares it with this value inside its own transaction.
    /// </summary>
    public int SessionVersion { get; set; }
}

/// <summary>What an account may do (column Rol, stored as "Kullanici" or "Admin").</summary>
public enum UserRole
{
    /// <summary>Rents films and sees and returns only their own rentals.</summary>
    User = 0,

    /// <summary>Manages films and accounts and sees and returns every rental.</summary>
    Admin = 1,
}

/// <summary>
/// How an account's stored password value has to be checked (column SifreBicimi). It is recorded
/// explicitly instead of being read from the value, because the original application let users choose
/// any text as a password, including text that looks like a hash.
/// </summary>
public enum PasswordFormat
{
    /// <summary>A PBKDF2 hash written by this application. Also the default for new accounts.</summary>
    Pbkdf2 = 0,

    /// <summary>
    /// The plain text password kept from a database of the original application. Replaced, together
    /// with the format, by a hash on the account's next successful sign-in.
    /// </summary>
    LegacyPlainText = 1,
}
