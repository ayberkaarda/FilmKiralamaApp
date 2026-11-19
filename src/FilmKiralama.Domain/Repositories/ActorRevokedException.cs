namespace FilmKiralama.Domain.Repositories;

/// <summary>
/// Raised by a repository when the account an operation acts for no longer exists, or no longer has
/// the role the operation needs. The stored account is checked inside the operation itself (in its
/// transaction, before anything is written), so a role taken away or an account deleted after the
/// sign-in takes effect at once, also for sessions that are still open. Nothing was written.
/// </summary>
public sealed class ActorRevokedException : Exception
{
    public ActorRevokedException()
        : base("The acting account no longer exists or no longer has the required role; nothing was changed.")
    {
    }

    public ActorRevokedException(string message)
        : base(message)
    {
    }

    public ActorRevokedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
