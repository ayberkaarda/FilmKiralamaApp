using System.Data;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FilmKiralama.Data.Persistence;

/// <summary>
/// Optional demonstration data, inserted only when it is asked for explicitly (FILM_DEMO=1 or
/// <c>Seed:Demo</c> = true) and only into a database without any account, film or rental. The
/// migrations themselves create no account, so a default installation has no known password; its first
/// admin is created by the first-run setup.
/// </summary>
/// <remarks>
/// The films, directors and people are fictional and have no posters; the e-mail addresses use the
/// reserved example.com domain and the phone numbers are not assigned to anyone. The password hashes
/// are precomputed so the data is deterministic (yonetici: "Demo.Yonetici1", ornek: "Demo.Kullanici1");
/// they are meant for trying the application out and must never be enabled on a real installation.
/// </remarks>
internal static class DemoData
{
    /// <summary>
    /// Adds the demonstration rows when the database is empty. Returns <see langword="false"/> when it
    /// already holds data; nothing is changed then. The emptiness check and the inserts share one
    /// serializable transaction (with update locks on SQL Server), so concurrent starts add the rows once.
    /// </summary>
    public static async Task<bool> SeedIfEmptyAsync(FilmDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            static async (context, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                if (!await IsEmptyAsync(context, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }

                // One row per save keeps the identity values in this order on a new database.
                await AddAsync(context, User("yonetici", UserRole.Admin, "yonetici@example.com", "5550000001", "PBKDF2-SHA256$600000$ASC2RxLiZ5HN+Ok1gIJLwA==$fBmT9hrrgCSl69UncVIVEXhOsmVTwVk0PyoPSaDGoC4="), token).ConfigureAwait(false);
                await AddAsync(context, User("ornek", UserRole.User, "ornek@example.com", "5550000002", "PBKDF2-SHA256$600000$gO4qpcCRGR90NQMl4qAgdw==$FKiy7eezb84NCNdzyYxHTr8cK9TNE5nRCjcIQotseJo="), token).ConfigureAwait(false);

                await AddAsync(context, Film("Sisli Liman", "Gerilim", 2019, "Ada Örnek", 25.00m, 3, "Kıyı kasabasında kaybolan bir yük gemisinin izini süren bir liman memurunun hikâyesi."), token).ConfigureAwait(false);
                await AddAsync(context, Film("Son Vapur", "Dram", 2015, "Mert Taslak", 18.50m, 2, "Gece yarısı kalkan son vapurda karşılaşan iki yabancının bir gecelik yolculuğu."), token).ConfigureAwait(false);
                await AddAsync(context, Film("Kayıp Harita", "Macera", 2021, "Ece Deneme", 30.00m, 1, "Dedesinden kalan eski bir haritayla dağlara çıkan üç arkadaşın keşif yolculuğu."), token).ConfigureAwait(false);
                await AddAsync(context, Film("Yıldız Tozu", "Bilim Kurgu", 2023, "Ada Örnek", 35.00m, 2, "Uzak bir istasyonda tek başına kalan bir mühendisin dünyaya dönüş çabası."), token).ConfigureAwait(false);
                await AddAsync(context, Film("Kuzey Rüzgârı", "Komedi", 2012, "Can Kurgu", 12.00m, 2, "Yanlışlıkla aynı kulübeyi kiralayan iki ailenin karlı bir hafta sonu."), token).ConfigureAwait(false);

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsEmptyAsync(FilmDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return !await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Films.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Rentals.AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT (SELECT COUNT(*) FROM dbo.Kullanicilar WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Filmler WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Kiralama WITH (UPDLOCK, HOLDLOCK)) AS [Value]
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows == 0;
    }

    private static async Task AddAsync<T>(FilmDbContext db, T entity, CancellationToken cancellationToken)
        where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static User User(string userName, UserRole role, string email, string phone, string hash) =>
        new() { UserName = userName, Role = role, Email = email, Phone = phone, PasswordHash = hash, PasswordFormat = PasswordFormat.Pbkdf2 };

    private static Film Film(string title, string genre, int year, string director, decimal dailyPrice, int copies, string about) =>
        new()
        {
            Title = title,
            Genre = genre,
            Year = year,
            Director = director,
            DailyPrice = dailyPrice,
            TotalCopies = copies,
            AvailableCopies = copies,
            IsListed = true,
            About = about,
        };
}
