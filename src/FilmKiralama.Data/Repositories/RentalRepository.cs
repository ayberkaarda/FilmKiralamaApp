using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Repositories;

internal sealed class RentalRepository(IDbContextFactory<FilmDbContext> contextFactory) : IRentalRepository
{
    public async Task<RentResult> RentAsync(RentalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Taken before anything is read: no other rental, return or stock change of this film can run
        // between the checks below and the commit.
        await Locks.FilmAsync(db, request.FilmId, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, request.UserId, admin: false, cancellationToken).ConfigureAwait(false);

        var film = await db.Films
            .AsNoTracking()
            .Where(f => f.Id == request.FilmId)
            .Select(f => new { f.IsListed, f.AvailableCopies, f.DailyPrice })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (film is null)
        {
            return new RentResult(RentOutcome.FilmNotFound, 0);
        }

        if (!film.IsListed || film.AvailableCopies <= 0)
        {
            return new RentResult(RentOutcome.NotAvailable, 0);
        }

        if (film.DailyPrice != request.ExpectedDailyPrice)
        {
            return new RentResult(RentOutcome.PriceChanged, 0);
        }

        if (await HasActiveRentalAsync(db, request.UserId, request.FilmId, cancellationToken).ConfigureAwait(false))
        {
            return new RentResult(RentOutcome.AlreadyRented, 0);
        }

        // The conditional UPDATE is the final word on the stock, so even a writer that skips the lock
        // can never take the last copy twice; the CHECK constraint backs it up.
        var taken = await db.Films
            .Where(f => f.Id == request.FilmId && f.IsListed && f.AvailableCopies > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AvailableCopies, f => f.AvailableCopies - 1), cancellationToken)
            .ConfigureAwait(false);
        if (taken != 1)
        {
            return new RentResult(RentOutcome.NotAvailable, 0);
        }

        var rental = new Rental
        {
            FilmId = request.FilmId,
            UserId = request.UserId,
            RentedOn = request.RentedOn,
            DueOn = request.DueOn,
            Status = RentalStatus.Active,
            Price = request.Price,
        };
        db.Rentals.Add(rental);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new RentResult(RentOutcome.Rented, rental.Id);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index rejected a second active rental of the same film by the same
            // account (a writer that skipped the lock); the copy goes back with the rollback.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await using var check = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            if (await HasActiveRentalAsync(check, request.UserId, request.FilmId, CancellationToken.None).ConfigureAwait(false))
            {
                return new RentResult(RentOutcome.AlreadyRented, 0);
            }

            throw;
        }
    }

    public async Task<ReturnResult> ReturnAsync(
        int rentalId,
        RentalActor actor,
        DateOnly returnedOn,
        Func<DateOnly?, decimal> lateFee,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lateFee);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Checked first, so a revoked admin learns nothing about other accounts' rentals.
        await Actors.RequireAsync(db, actor.UserId, actor.AsAdmin, cancellationToken).ConfigureAwait(false);
        var ownerId = OwnerOf(actor);

        // The film decides which lock to take; a rental of another account is the same as none.
        var filmId = await Owned(db.Rentals.AsNoTracking(), ownerId)
            .Where(r => r.Id == rentalId && r.Status == RentalStatus.Active)
            .Select(r => (int?)r.FilmId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (filmId is null)
        {
            return new ReturnResult(ReturnOutcome.NotFound, null, 0m);
        }

        await Locks.FilmAsync(db, filmId.Value, cancellationToken).ConfigureAwait(false);

        // Read again under the lock: a concurrent return of the same rental has finished by now.
        var rental = await Owned(db.Rentals.AsNoTracking(), ownerId)
            .Where(r => r.Id == rentalId && r.Status == RentalStatus.Active)
            .Select(r => new { r.FilmId, r.DueOn })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rental is null)
        {
            return new ReturnResult(ReturnOutcome.NotFound, null, 0m);
        }

        var fee = lateFee(rental.DueOn);
        decimal? storedFee = fee > 0m ? fee : null;
        var closed = await db.Rentals
            .Where(r => r.Id == rentalId && r.Status == RentalStatus.Active)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, RentalStatus.Returned)
                    .SetProperty(r => r.ReturnedOn, returnedOn)
                    .SetProperty(r => r.LateFee, storedFee),
                cancellationToken)
            .ConfigureAwait(false);
        if (closed != 1)
        {
            return new ReturnResult(ReturnOutcome.NotFound, null, 0m);
        }

        var restocked = await db.Films
            .Where(f => f.Id == rental.FilmId && f.AvailableCopies < f.TotalCopies)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AvailableCopies, f => f.AvailableCopies + 1), cancellationToken)
            .ConfigureAwait(false);
        if (restocked != 1)
        {
            // Every active rental holds a copy, so the shelf can never be full here.
            throw new InvalidOperationException("The film's stock does not match its active rentals; the return was not saved.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ReturnResult(ReturnOutcome.Returned, rental.DueOn, fee);
    }

    public async Task<IReadOnlyList<RentalDetails>> GetActiveAsync(RentalActor actor, FilmFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor.UserId, actor.AsAdmin, cancellationToken).ConfigureAwait(false);
        var query =
            from rental in Owned(db.Rentals.AsNoTracking(), OwnerOf(actor))
            join film in FilmRepository.Matching(db.Films.AsNoTracking(), filter) on rental.FilmId equals film.Id
            join user in db.Users.AsNoTracking() on rental.UserId equals user.Id
            where rental.Status == RentalStatus.Active
            orderby rental.Id
            select new RentalDetails(
                rental.Id,
                film.Id,
                film.Title,
                film.Genre,
                film.Year,
                film.Director,
                film.ImageUrl,
                user.Id,
                user.UserName,
                rental.RentedOn,
                rental.DueOn,
                rental.Price,
                rental.Status);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The account whose rentals <paramref name="actor"/> reaches, or <see langword="null"/> (every account) for an admin.</summary>
    private static int? OwnerOf(RentalActor actor) => actor.AsAdmin ? null : actor.UserId;

    private static IQueryable<Rental> Owned(IQueryable<Rental> rentals, int? ownerId) =>
        ownerId is { } owner ? rentals.Where(r => r.UserId == owner) : rentals;

    private static Task<bool> HasActiveRentalAsync(FilmDbContext db, int userId, int filmId, CancellationToken cancellationToken) =>
        db.Rentals.AnyAsync(r => r.UserId == userId && r.FilmId == filmId && r.Status == RentalStatus.Active, cancellationToken);
}
