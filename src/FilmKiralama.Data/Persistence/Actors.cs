using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>
/// Checks the account an operation acts for against the stored account, inside the operation. A
/// session remembers the role and the session version it signed in with; this check makes the stored
/// account decide instead, at the moment of the operation: whether it still exists, still has the
/// session version (no new password and no new role since the sign-in) and, where needed, the admin role.
/// </summary>
internal static class Actors
{
    /// <summary>
    /// Throws <see cref="ActorRevokedException"/> unless account <paramref name="actor"/> exists, still has
    /// the session version of the actor's session and, with <paramref name="admin"/>, has the admin role.
    /// Called inside the caller's transaction before anything is read or written. On SQL Server the
    /// account row stays share-locked until that transaction ends (HOLDLOCK, a locking read also under
    /// read committed snapshot), so a concurrent demotion, password change or deletion of the actor waits
    /// for the operation to finish instead of slipping in between the check and the writes.
    /// </summary>
    public static async Task RequireAsync(FilmDbContext db, Actor actor, bool admin, CancellationToken cancellationToken)
    {
        var (actorId, version) = actor;
        bool valid;
        if (db.Database.IsSqlServer())
        {
            var needsAdmin = admin ? 1 : 0;
            var adminRole = SchemaLimits.AdminRoleValue;
            valid = await db.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM dbo.Kullanicilar WITH (HOLDLOCK) WHERE KullaniciID = {actorId} AND OturumSurumu = {version} AND ({needsAdmin} = 0 OR Rol = {adminRole})")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false) == 1;
        }
        else
        {
            valid = await db.Users
                .AnyAsync(u => u.Id == actorId && u.SessionVersion == version && (!admin || u.Role == UserRole.Admin), cancellationToken)
                .ConfigureAwait(false);
        }

        if (!valid)
        {
            throw new ActorRevokedException();
        }
    }
}
