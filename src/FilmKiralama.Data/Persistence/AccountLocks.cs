using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>Locking reads of the first-admin setup, taken inside its serializable transaction.</summary>
internal static class AccountLocks
{
    // The expiry is compared with the database's own clock, the one docs/issue-setup-key.sql sets
    // SonKullanma with, so the computer's clock cannot keep an expired key usable.
    private const string UsableSetupKeys =
        "SELECT * FROM dbo.KurulumAnahtarlari WITH (UPDLOCK, HOLDLOCK) WHERE Tuketildi = 0 AND SonKullanma > SYSUTCDATETIME()";

    /// <summary>
    /// The setup keys usable now (unused and unexpired), locked until the commit on SQL Server. Every
    /// caller holds the account table lock already, so this lock only guards the rows against writers
    /// that do not take it, such as the issuing script revoking a key. On SQL Server "now" is the
    /// database's UTC clock; other providers (the tests' SQLite) have none and use
    /// <paramref name="clientNow"/>, relying on the serializable transaction alone.
    /// </summary>
    public static Task<List<StoredSetupKey>> UsableSetupKeysLockedAsync(FilmDbContext db, DateTime clientNow, CancellationToken cancellationToken)
    {
        var keys = db.Database.IsSqlServer()
            ? db.SetupKeys.FromSqlRaw(UsableSetupKeys)
            : db.SetupKeys.Where(k => !k.Consumed && k.ExpiresAt > clientNow);
        return keys
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => new StoredSetupKey(k.Id, k.KeyHash))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Reads whether the account table is empty, has accounts only or has an admin, locking all of it until the commit.</summary>
    public static async Task<AccountState> AccountStateLockedAsync(FilmDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken).ConfigureAwait(false))
            {
                return AccountState.AdminExists;
            }

            return await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false) ? AccountState.UsersOnly : AccountState.Empty;
        }

        // One scan of the whole table under UPDLOCK and HOLDLOCK: 0 empty, 1 accounts only, 2 an admin.
        var state = await db.Database
            .SqlQueryRaw<int>("SELECT ISNULL(MAX(CASE WHEN Rol = N'Admin' THEN 2 ELSE 1 END), 0) AS [Value] FROM dbo.Kullanicilar WITH (UPDLOCK, HOLDLOCK)")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return (AccountState)state;
    }
}

internal enum AccountState
{
    Empty = 0,
    UsersOnly = 1,
    AdminExists = 2,
}
