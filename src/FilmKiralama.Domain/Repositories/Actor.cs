namespace FilmKiralama.Domain.Repositories;

/// <summary>
/// The signed-in account an operation acts for, with the session version its session was issued with.
/// The repository checks both against the stored account inside the operation's transaction: an account
/// that was deleted, or whose role or password changed since the session began (also when the change
/// was undone again), reads and writes nothing.
/// </summary>
public readonly record struct Actor(int UserId, int SessionVersion);
