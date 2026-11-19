namespace FilmKiralama.Business.Security;

/// <summary>Brute force protection: an account is locked for a while after repeated failures.</summary>
public static class LoginPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}

/// <summary>Password length rules.</summary>
public static class PasswordPolicy
{
    /// <summary>Shortest password that can be chosen.</summary>
    public const int MinLength = 8;

    /// <summary>Longest password that can be chosen; also the longest one accepted at sign-in.</summary>
    /// <remarks>The original application stored passwords in NVARCHAR(50), so every existing password fits.</remarks>
    public const int MaxLength = 64;
}
