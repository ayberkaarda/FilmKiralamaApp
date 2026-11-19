namespace FilmKiralama.Domain.Repositories;

/// <summary>
/// The one clock every stored time decision is made with: sign-in locks, the start, due and return day
/// of a rental and what is overdue. With SQL Server it is the database server's UTC clock, so computers
/// whose own clocks are wrong still agree on whether an account is locked or a copy is late; the
/// computer's time zone only turns that instant into a local calendar day.
/// </summary>
public interface IClock
{
    /// <summary>The current UTC time (<see cref="DateTimeKind.Utc"/>).</summary>
    Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default);
}
