using FilmKiralama.Business.Auditing;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Services;

/// <summary>Role checks and the local calendar day shared by the services.</summary>
/// <remarks>
/// The role a session signed in with only decides what it may ask for. Whether the account still
/// exists, still has the session's version (no new password or role since the sign-in) and still has
/// that role is decided by the repositories against the stored account, inside each operation
/// (<see cref="ActorRevokedException"/>), and <see cref="AsActorAsync{T}"/> turns a refusal there into
/// the one answer that asks the user to sign in again.
/// </remarks>
internal static class Access
{
    public static void RequireSignedIn(UserSession? session) => ArgumentNullException.ThrowIfNull(session);

    /// <summary>Refuses, and records, an operation of a session that is not an admin.</summary>
    public static void RequireAdmin(UserSession? session, IAuditLog audit)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsAdmin)
        {
            audit.Record(AuditEvent.AccessDenied, AuditCodes.Of(session.Role));
            throw new AccessDeniedException();
        }
    }

    /// <summary>
    /// The account a rental operation of the session acts for: an admin reaches every account's
    /// rentals, anyone else their own.
    /// </summary>
    public static RentalActor RentalActorOf(UserSession session) => new(session.UserId, session.IsAdmin, session.SessionVersion);

    /// <summary>
    /// Runs a repository call for <paramref name="session"/>. When the stored account no longer exists
    /// or no longer has the role (nothing was written), the call ends with an
    /// <see cref="AccessDeniedException"/> carrying <see cref="AccessDeniedException.SessionRevokedMessage"/>.
    /// </summary>
    public static async Task<T> AsActorAsync<T>(this IAuditLog audit, UserSession session, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ActorRevokedException ex)
        {
            audit.Record(AuditEvent.SessionRevoked, AuditCodes.Of(session.Role));
            throw new AccessDeniedException(AccessDeniedException.SessionRevokedMessage, ex);
        }
    }

    /// <summary>Today's date on the local wall clock; rental days are local calendar days.</summary>
    public static DateOnly Today(this TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
}
