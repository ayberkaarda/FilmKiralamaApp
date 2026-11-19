using FilmKiralama.Business.Auditing;

namespace FilmKiralama.Business.Services;

/// <summary>Role checks and the local calendar day shared by the services.</summary>
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
    /// The account whose rentals the session may see and return: its own id, or <see langword="null"/>
    /// (everyone) for an admin.
    /// </summary>
    public static int? OwnerFilter(UserSession session) => session.IsAdmin ? null : session.UserId;

    /// <summary>Today's date on the local wall clock; rental days are local calendar days.</summary>
    public static DateOnly Today(this TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
}
