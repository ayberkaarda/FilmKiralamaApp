using System.Data;
using System.Data.Common;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class UserRepository(IDbContextFactory<FilmDbContext> contextFactory) : IUserRepository
{
    public async Task<AccountCredentials?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.UserName == userName)
            .Select(u => new AccountCredentials(u.Id, u.UserName, u.Role, u.PasswordHash, u.PasswordFormat, u.FailedLoginCount, u.LockoutEnd, u.SessionVersion))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccountCredentials?> FindByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccountCredentials(u.Id, u.UserName, u.Role, u.PasswordHash, u.PasswordFormat, u.FailedLoginCount, u.LockoutEnd, u.SessionVersion))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserSummary>> GetAllAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => new UserSummary(u.Id, u.UserName, u.Email, u.Phone, u.Role, u.SessionVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccountFields> FindTakenAsync(
        string userName,
        string? email,
        string? phone,
        int? exceptUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var others = db.Users.AsNoTracking().Where(u => exceptUserId == null || u.Id != exceptUserId);

        var taken = AccountFields.None;
        if (await others.AnyAsync(u => u.UserName == userName, cancellationToken).ConfigureAwait(false))
        {
            taken |= AccountFields.UserName;
        }

        if (email is not null && await others.AnyAsync(u => u.Email == email, cancellationToken).ConfigureAwait(false))
        {
            taken |= AccountFields.Email;
        }

        if (phone is not null && await others.AnyAsync(u => u.Phone == phone, cancellationToken).ConfigureAwait(false))
        {
            taken |= AccountFields.Phone;
        }

        return taken;
    }

    public async Task<int> AddAsync(User user, Actor? actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (actor is { } admin)
            {
                await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);
            }

            var entity = new User
            {
                UserName = user.UserName,
                PasswordHash = user.PasswordHash,
                PasswordFormat = user.PasswordFormat,
                Role = user.Role,
                Email = user.Email,
                Phone = user.Phone,
            };
            db.Users.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return entity.Id;
        }
        catch (DbUpdateException ex)
        {
            // A unique index rejected a value registered concurrently. Checked with a new query, after the
            // transaction has ended, so the provider specific error code does not matter.
            await ThrowIfTakenAsync(user.UserName, user.Email, user.Phone, null, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AccountUpdateOutcome> UpdateAsync(Actor actor, AccountUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await Locks.AdminRolesAsync(db, cancellationToken).ConfigureAwait(false);

            // Decided under the lock that every role change takes: an admin demoted concurrently has lost
            // the role here already, so it can neither change accounts nor give the role back to itself.
            await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);

            var id = update.UserId;
            var expectedVersion = update.ExpectedVersion;
            var role = update.Role;
            var keepsAdmin = role == UserRole.Admin;
            var hash = update.NewPasswordHash;
            var newPassword = hash is not null;

            // The WHERE clause is the final decision: the account still has the session version the form
            // loaded (no role or password change since, so a stale form cannot give back a role another
            // admin just took away), and an admin keeps the role or another admin exists. The admin role
            // lock above keeps a concurrent demotion from passing the same check.
            var updated = await db.Users
                .Where(u => u.Id == id
                    && u.SessionVersion == expectedVersion
                    && (keepsAdmin || u.Role != UserRole.Admin || db.Users.Any(o => o.Id != id && o.Role == UserRole.Admin)))
                .ExecuteUpdateAsync(
                    s =>
                    {
                        // A new role or password ends every open session of the account for good: the
                        // version is computed from the row before the SET, so it rises exactly when the
                        // stored role differs or a password is set.
                        s.SetProperty(u => u.SessionVersion, u => newPassword || u.Role != role ? u.SessionVersion + 1 : u.SessionVersion)
                            .SetProperty(u => u.UserName, update.UserName)
                            .SetProperty(u => u.Email, update.Email)
                            .SetProperty(u => u.Phone, update.Phone)
                            .SetProperty(u => u.Role, role);
                        if (hash is not null)
                        {
                            // A password set by an admin also lifts a lock.
                            s.SetProperty(u => u.PasswordHash, hash)
                                .SetProperty(u => u.PasswordFormat, PasswordFormat.Pbkdf2)
                                .SetProperty(u => u.FailedLoginCount, 0)
                                .SetProperty(u => u.LockoutEnd, (DateTime?)null);
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated == 1)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return AccountUpdateOutcome.Updated;
            }

            var version = await db.Users
                .Where(u => u.Id == id)
                .Select(u => (int?)u.SessionVersion)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return version switch
            {
                null => AccountUpdateOutcome.NotFound,
                _ when version != expectedVersion => AccountUpdateOutcome.ChangedElsewhere,
                _ => AccountUpdateOutcome.LastAdmin,
            };
        }
        catch (DbUpdateException ex)
        {
            await ThrowIfTakenAsync(update.UserName, update.Email, update.Phone, update.UserId, ex).ConfigureAwait(false);
            throw;
        }
        catch (DbException ex)
        {
            // ExecuteUpdate reports a unique index violation as the provider's exception.
            await ThrowIfTakenAsync(update.UserName, update.Email, update.Phone, update.UserId, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AccountDeleteOutcome> DeleteAsync(Actor actor, int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);

        // One statement decides: no admin, no rental history.
        var deleted = await db.Users
            .Where(u => u.Id == userId && u.Role != UserRole.Admin && !db.Rentals.Any(r => r.UserId == userId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 1)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AccountDeleteOutcome.Deleted;
        }

        var role = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => (UserRole?)u.Role)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return role switch
        {
            null => AccountDeleteOutcome.NotFound,
            UserRole.Admin => AccountDeleteOutcome.IsAdmin,
            _ => AccountDeleteOutcome.HasRentals,
        };
    }

    public async Task<int?> ChangePasswordAsync(AccountCredentials verified, string newPasswordHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentException.ThrowIfNullOrEmpty(newPasswordHash);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var id = verified.Id;
        var verifiedHash = verified.PasswordHash;
        var verifiedFormat = verified.PasswordFormat;
        var verifiedVersion = verified.SessionVersion;
        var newVersion = verifiedVersion + 1;

        // The WHERE clause is the final decision: the password and the session version are still the ones
        // that were verified, so a concurrent reset, role change or other password change wins.
        var updated = await db.Users
            .Where(u => u.Id == id && u.PasswordHash == verifiedHash && u.PasswordFormat == verifiedFormat && u.SessionVersion == verifiedVersion)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.PasswordHash, newPasswordHash)
                    .SetProperty(u => u.PasswordFormat, PasswordFormat.Pbkdf2)
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.SessionVersion, newVersion),
                cancellationToken)
            .ConfigureAwait(false);
        return updated == 1 ? newVersion : null;
    }

    public async Task<bool> AnyAdminAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AnyUserAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirstAdminResult> AddFirstAdminAsync(
        User admin,
        SetupKeyAttempt? setupKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admin);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Serializable: the account check holds a range lock on the whole table until the commit, so
            // two concurrent setups can never both insert and no account can appear between the check and
            // the insert. On SQL Server the check takes an update lock, which a second setup has to wait
            // for, so the loser sees the winner's row instead of becoming a deadlock victim.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            var state = await AccountLocks.AccountStateLockedAsync(db, cancellationToken).ConfigureAwait(false);
            if (state == AccountState.AdminExists)
            {
                return new FirstAdminResult(FirstAdminStatus.AdminExists, 0);
            }

            if (state == AccountState.UsersOnly)
            {
                if (setupKey is null)
                {
                    return new FirstAdminResult(FirstAdminStatus.KeyRequired, 0);
                }

                // A taken name is refused before the authority is asked, so it does not use up the key.
                var name = admin.UserName;
                if (await db.Users.AnyAsync(u => u.UserName == name, cancellationToken).ConfigureAwait(false))
                {
                    throw new DuplicateAccountException(AccountFields.UserName);
                }

                // Decided while the lock is held: only the setup that gets here first can use the key, and
                // the key is used up in this transaction, so a failed insert or commit leaves it unused.
                // A wrong key writes nothing and is rolled back with the rest.
                if (!await UseSetupKeyAsync(db, setupKey, cancellationToken).ConfigureAwait(false))
                {
                    return new FirstAdminResult(FirstAdminStatus.NotAuthorized, 0);
                }
            }

            var entity = new User
            {
                UserName = admin.UserName,
                PasswordHash = admin.PasswordHash,
                PasswordFormat = admin.PasswordFormat,
                Role = UserRole.Admin,
                Email = admin.Email,
                Phone = admin.Phone,
            };
            db.Users.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new FirstAdminResult(FirstAdminStatus.Created, entity.Id);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException || ex.InnerException is DbUpdateException or DbException)
        {
            // Lost the race (deadlock victim or a rejected insert): the winner's row decides; otherwise
            // the unique index rejected a name that another account already has.
            if (await AnyAdminAsync(CancellationToken.None).ConfigureAwait(false))
            {
                return new FirstAdminResult(FirstAdminStatus.AdminExists, 0);
            }

            await ThrowIfTakenAsync(admin.UserName, admin.Email, admin.Phone, null, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task RecordFailedLoginAsync(int userId, int maxAttempts, DateTime lockoutEnd, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // One UPDATE statement, so concurrent failures cannot lose increments.
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.LockoutEnd, u => u.FailedLoginCount + 1 >= maxAttempts ? lockoutEnd : u.LockoutEnd)
                    .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= maxAttempts ? 0 : u.FailedLoginCount + 1),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RecordSuccessfulLoginAsync(
        AccountCredentials verified,
        DateTime now,
        string? newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // The WHERE clause is the final decision: a lock set by a concurrent failure, or a password
        // changed since it was verified, makes the statement match no row and the sign-in fails.
        var id = verified.Id;
        var verifiedHash = verified.PasswordHash;
        var verifiedFormat = verified.PasswordFormat;
        var hash = newPasswordHash ?? verifiedHash;
        var format = newPasswordHash is null ? verifiedFormat : PasswordFormat.Pbkdf2;

        var updated = await db.Users
            .Where(u => u.Id == id
                && (u.LockoutEnd == null || u.LockoutEnd <= now)
                && u.PasswordHash == verifiedHash
                && u.PasswordFormat == verifiedFormat)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTime?)null)
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordFormat, format),
                cancellationToken)
            .ConfigureAwait(false);

        return updated == 1;
    }

    /// <summary>Throws <see cref="DuplicateAccountException"/> when another account holds one of the values.</summary>
    private async Task ThrowIfTakenAsync(string userName, string? email, string? phone, int? exceptUserId, Exception cause)
    {
        var taken = await FindTakenAsync(userName, email, phone, exceptUserId, CancellationToken.None).ConfigureAwait(false);
        if (taken != AccountFields.None)
        {
            throw new DuplicateAccountException(taken, cause);
        }
    }

    /// <summary>
    /// Checks <paramref name="attempt"/> against the usable setup keys and, when one matches, uses it up
    /// with a conditional UPDATE of Tuketildi and TuketimTarihi (the only columns the application's
    /// login may change) inside the caller's transaction, so the use commits or rolls back with the
    /// admin write. A wrong key writes nothing: there is no attempt counter that a guesser could exhaust.
    /// </summary>
    private static async Task<bool> UseSetupKeyAsync(FilmDbContext db, SetupKeyAttempt attempt, CancellationToken cancellationToken)
    {
        var keys = await AccountLocks.UsableSetupKeysLockedAsync(db, attempt.Now, cancellationToken).ConfigureAwait(false);
        if (attempt.FindMatch(keys) is not { } id)
        {
            return false;
        }

        // The WHERE clause is the final decision: of two transactions with the same key only one can
        // match the unused row.
        if (db.Database.IsSqlServer())
        {
            // The expiry is decided again at the moment of use, against the database's clock (the one
            // the issuing script set SonKullanma with), and the use is stamped with it: a key that
            // expired after it was read matches no row, and the computer's clock plays no part.
            var used = await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE dbo.KurulumAnahtarlari
                SET Tuketildi = 1, TuketimTarihi = SYSUTCDATETIME()
                WHERE AnahtarID = {id} AND Tuketildi = 0 AND SonKullanma > SYSUTCDATETIME()
                """,
                cancellationToken).ConfigureAwait(false);
            return used == 1;
        }

        // Other providers (the tests' SQLite) have no database clock and use the attempt's time.
        var now = attempt.Now;
        var consumed = await db.SetupKeys
            .Where(k => k.Id == id && !k.Consumed && k.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(k => k.Consumed, true)
                    .SetProperty(k => k.ConsumedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        return consumed == 1;
    }
}
