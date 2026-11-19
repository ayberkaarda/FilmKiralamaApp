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
    /// <summary>SqlClient's command timeout when none is configured.</summary>
    private const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>How much longer than the lock wait the lock command may run before the client gives up.</summary>
    private const int CommandTimeoutMarginSeconds = 15;

    /// <summary>
    /// Takes the lock on SQL Server. Other providers (the tests' SQLite) serialize writing transactions
    /// themselves and need none. The caller must have begun a transaction.
    /// </summary>
    /// <remarks>
    /// The lock command runs with a command timeout longer than <paramref name="timeoutMilliseconds"/>,
    /// so a lock that stays taken ends as a refused lock (<see cref="AppLockNotGrantedException"/>,
    /// "busy") and never as a client side command timeout that looks like a database failure.
    /// </remarks>
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

        var configured = db.Database.GetCommandTimeout();
        var needed = CommandTimeoutFor(timeoutMilliseconds, configured);
        db.Database.SetCommandTimeout(needed);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
                [result, name, timeout],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.Database.SetCommandTimeout(configured);
        }

        EnsureGranted(result.Value is int code ? code : null, resource);
    }

    /// <summary>
    /// The command timeout (seconds) for a lock request that waits <paramref name="lockTimeoutMilliseconds"/>:
    /// the configured one when it is long enough, otherwise the wait plus a margin.
    /// </summary>
    internal static int CommandTimeoutFor(int lockTimeoutMilliseconds, int? configuredSeconds)
    {
        var current = configuredSeconds ?? DefaultCommandTimeoutSeconds;
        if (current == 0)
        {
            return 0; // No limit.
        }

        var needed = ((lockTimeoutMilliseconds + 999) / 1000) + CommandTimeoutMarginSeconds;
        return Math.Max(current, needed);
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

    /// <summary>Serializes the additions to one account's watchlist, so its size limit and its duplicate check hold.</summary>
    public static Task WatchlistAsync(FilmDbContext db, int userId, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "FilmKiralama.Watchlist." + userId.ToString(CultureInfo.InvariantCulture), TimeoutMilliseconds, cancellationToken);

    /// <summary>Serializes changes of the admin role, so two concurrent demotions cannot remove the last admin.</summary>
    public static Task AdminRolesAsync(FilmDbContext db, CancellationToken cancellationToken) =>
        AppLock.AcquireAsync(db, "FilmKiralama.AdminRoles", TimeoutMilliseconds, cancellationToken);
}
