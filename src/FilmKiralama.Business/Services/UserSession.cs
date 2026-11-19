using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Services;

/// <summary>
/// The signed-in account. Only <see cref="AuthService"/> and <see cref="SetupService"/> create sessions
/// (the constructor is internal), and every service takes one and checks the role and the ownership of
/// the data against it, so the user interface cannot widen access by passing other ids.
/// </summary>
public sealed class UserSession
{
    private int _sessionVersion;

    internal UserSession(int userId, string userName, UserRole role, int sessionVersion)
    {
        UserId = userId;
        UserName = userName;
        Role = role;
        _sessionVersion = sessionVersion;
    }

    public int UserId { get; }

    public string UserName { get; }

    public UserRole Role { get; }

    /// <summary>
    /// The account's session version at sign-in. Once the stored version is raised (a new role, a password
    /// set by an admin, or the account's own password change in another session) every operation of this
    /// session is refused for good, also when the change is undone. The session that changes its own
    /// password moves on to the new version (see <see cref="Renew"/>).
    /// </summary>
    public int SessionVersion => Volatile.Read(ref _sessionVersion);

    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>The account as the repositories check it inside each operation.</summary>
    internal Actor Actor => new(UserId, SessionVersion);

    /// <summary>
    /// Moves this session to <paramref name="sessionVersion"/>, the version its own password change just
    /// stored. Every window of this sign-in shares the session and keeps working; every other session of
    /// the account still carries the old version and is refused from now on.
    /// </summary>
    internal void Renew(int sessionVersion) => Volatile.Write(ref _sessionVersion, sessionVersion);
}
