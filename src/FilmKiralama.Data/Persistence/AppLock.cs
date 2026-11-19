using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>
/// Exclusive SQL Server application locks (sp_getapplock) owned by the current transaction and released
/// when it ends. sp_getapplock reports a refused lock through its return code instead of an error, so
/// the code is always checked and the caller stops before it reads or writes anything.
/// </summary>
internal static class AppLock
{
    /// <summary>
    /// Takes the lock on SQL Server. Other providers (the tests' SQLite) serialize writing transactions
    /// themselves and need none. The caller must have begun a transaction.
    /// </summary>
    public static async Task AcquireAsync(FilmDbContext db, string resource, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var name = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource };
        var timeout = new SqlParameter("@timeout", SqlDbType.Int) { Value = timeoutMilliseconds };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
            [result, name, timeout],
            cancellationToken).ConfigureAwait(false);

        EnsureGranted(result.Value is int code ? code : null, resource);
    }

    /// <summary>Throws when an sp_getapplock return code means the lock was not granted (0 and 1 mean granted).</summary>
    internal static void EnsureGranted(int? returnCode, string resource)
    {
        if (returnCode is >= 0)
        {
            return;
        }

        var reason = returnCode switch
        {
            -1 => "the request timed out because another operation still holds it",
            -2 => "the request was canceled",
            -3 => "the request was chosen as a deadlock victim",
            -999 => "the lock request was invalid",
            null => "the lock procedure returned no result",
            _ => "the lock procedure failed",
        };

        throw new AppLockNotGrantedException(
            $"The application lock '{resource}' could not be acquired ({reason}, sp_getapplock returned "
            + $"{returnCode?.ToString(CultureInfo.InvariantCulture) ?? "null"}). No changes were made; try again later.");
    }
}

/// <summary>Raised when an application lock was refused; the operation made no change.</summary>
public sealed class AppLockNotGrantedException : Exception
{
    public AppLockNotGrantedException()
    {
    }

    public AppLockNotGrantedException(string message)
        : base(message)
    {
    }

    public AppLockNotGrantedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Named locks of the application.</summary>
internal static class Locks
{
    private const int TimeoutMilliseconds = 30_000;

    /// <summary>
    /// Serializes the operations that move a film's copies (rent, return, change of the number of
    /// copies, delete): each takes this lock first inside its transaction and only then reads the state
    /// it decides on, so no other such operation on the same film can change that state before it commits.
    /// </summary>
    public static Task FilmAsync(FilmDbContext db, int filmId, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "FilmKiralama.Film." + filmId.ToString(CultureInfo.InvariantCulture), TimeoutMilliseconds, cancellationToken);

    /// <summary>Serializes changes of the admin role, so two concurrent demotions cannot remove the last admin.</summary>
    public static Task AdminRolesAsync(FilmDbContext db, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "FilmKiralama.AdminRoles", TimeoutMilliseconds, cancellationToken);
}
