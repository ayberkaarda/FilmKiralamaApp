using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FilmKiralama.Business;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FilmKiralama.Data.Tests;

/// <summary>Shared registrations and sample values for tests that run the real services on a real database.</summary>
internal static class TestServices
{
    /// <summary>Monday 2 March 2026, 10:10; the fake clock's local time zone is UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 3, 2, 10, 10, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 3, 2);

    // Demonstration data (DemoData, with demo mode on): "yonetici" (1, admin) and "ornek" (2, user);
    // films 1-5, film 3 with a single copy, film 1 with three.
    public const int SeedAdminId = 1;
    public const int SeedUserId = 2;
    public const int ThreeCopiesFilmId = 1;
    public const int SingleCopyFilmId = 3;

    /// <summary>The late fee per day the tests configure.</summary>
    public const decimal DailyLateFee = 10m;

    public const string Password = "Gizli.Sifre1";

    /// <summary>
    /// Adds the business services with a fake clock and a cheaper password hash (new hashes only; the
    /// seeded hashes keep their own iteration count).
    /// </summary>
    public static IServiceCollection AddTestBusinessServices(this IServiceCollection services, FakeTimeProvider clock)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(iterations: 1_000));
        services.AddSingleton(new SetupKeyThrottle(TimeSpan.Zero));
        services.AddSingleton(new SetupKeyEnvironment(null));
        services.AddBusinessServices(new RentalPolicy { DailyLateFee = DailyLateFee, MaxRentalDays = 30 });
        return services;
    }

    public static UserSession User(int id) => new(id, "kullanici" + id.ToString(CultureInfo.InvariantCulture), UserRole.User, sessionVersion: 0);

    public static UserSession Admin(int id) => new(id, "yonetici" + id.ToString(CultureInfo.InvariantCulture), UserRole.Admin, sessionVersion: 0);

    /// <summary>
    /// A session of account <paramref name="userId"/> with its stored role and session version, as a new
    /// sign-in would issue it now.
    /// </summary>
    public static async Task<UserSession> CurrentSessionAsync(IServiceProvider provider, int userId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await provider.GetRequiredService<IDbContextFactory<FilmDbContext>>().CreateDbContextAsync(ct);
        var account = await db.Users.Where(u => u.Id == userId).Select(u => new { u.UserName, u.Role, u.SessionVersion }).SingleAsync(ct);
        return new UserSession(userId, account.UserName, account.Role, account.SessionVersion);
    }

    /// <summary>Registers a customer through the real service and returns its session.</summary>
    public static async Task<UserSession> RegisterAsync(IServiceProvider provider, string name, int number)
    {
        var ct = TestContext.Current.CancellationToken;
        var phone = "554" + number.ToString("0000000", CultureInfo.InvariantCulture);
        await provider.GetRequiredService<IAuthService>().RegisterAsync(name, Password, name + "@example.com", phone, ct);
        return await provider.GetRequiredService<IAuthService>().SignInAsync(name, Password, ct);
    }

    /// <summary>
    /// Adds a film with <paramref name="copies"/> copies through the real service, as <paramref name="admin"/>
    /// (the seeded admin by default), and returns its id.
    /// </summary>
    public static Task<int> AddFilmAsync(IServiceProvider provider, string title, int copies, string dailyPrice = "10", UserSession? admin = null) =>
        provider.GetRequiredService<IFilmService>().AddAsync(
            admin ?? Admin(SeedAdminId),
            new FilmInput(title, "Dram", "2020", "Ece Deneme", dailyPrice, null, "Deneme filmi.", copies, true),
            TestContext.Current.CancellationToken);

    /// <summary>Rents <paramref name="filmId"/> for <paramref name="days"/> days at its current price.</summary>
    public static async Task<RentalReceipt> RentAsync(IServiceProvider provider, UserSession session, int filmId, int days = 3)
    {
        var ct = TestContext.Current.CancellationToken;
        var film = await provider.GetRequiredService<IFilmService>().GetAsync(session, filmId, ct);
        var rentals = provider.GetRequiredService<IRentalService>();
        return await rentals.RentAsync(session, await rentals.QuoteAsync(filmId, film.DailyPrice, days, ct), ct);
    }

    /// <summary>
    /// Stores a new setup key as the database owner's script does (16 random bytes, SHA-256 of the 32
    /// hexadecimal characters, 24 hours from <paramref name="now"/>) and returns it in groups of four.
    /// On SQL Server the tests run the script itself; this stands in for it where there is no T-SQL.
    /// </summary>
    public static async Task<string> AddSetupKeyAsync(IServiceProvider provider, DateTimeOffset now)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using var db = await provider.GetRequiredService<IDbContextFactory<FilmDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.SetupKeys.Add(new SetupKey
        {
            KeyHash = SHA256.HashData(Encoding.ASCII.GetBytes(key)),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.UtcDateTime + SetupKeyPolicy.Lifetime,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return SetupKeyPolicy.Format(key);
    }
}
