using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>
/// EF Core context of the film rental database. Table and column names follow the original schema
/// (Kullanicilar, Filmler, Kiralama) and its Turkish naming for the new parts.
/// </summary>
public sealed class FilmDbContext(DbContextOptions<FilmDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Film> Films => Set<Film>();

    public DbSet<Rental> Rentals => Set<Rental>();

    public DbSet<SetupKey> SetupKeys => Set<SetupKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FilmDbContext).Assembly);
    }
}
