using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>Runs against a real SQL Server instance; see <see cref="SqlServerTestDatabase"/>.</summary>
public sealed class LegacyUpgradeTests
{
    private const string BackupEnvironmentVariable = "FILM_LEGACY_BACKUP";

    // Schema of the original application's database (read from its backup): system-named primary keys,
    // named defaults for the role ("Müsteri") and the film status ("Müsait"), plain text passwords in
    // NVARCHAR(50), nullable film columns, a foreign key only from Kiralama to Kullanicilar, no history.
    internal const string LegacySchema = """
        CREATE TABLE dbo.Kullanicilar (
            KullaniciID INT IDENTITY(1,1) PRIMARY KEY,
            KullaniciAdi NVARCHAR(50) NOT NULL,
            Sifre NVARCHAR(50) NOT NULL,
            Rol NVARCHAR(20) NULL CONSTRAINT DF_Kullanicilar_Rol DEFAULT 'Müsteri',
            Eposta NVARCHAR(100) NULL,
            Telefon VARCHAR(15) NULL);
        CREATE TABLE dbo.Filmler (
            FilmID INT IDENTITY(1,1) PRIMARY KEY,
            Ad NVARCHAR(50) NULL,
            Tur NVARCHAR(50) NULL,
            Yil INT NULL,
            Yonetmen NVARCHAR(50) NULL,
            GunlukUcret DECIMAL(10, 2) NULL,
            Durum NVARCHAR(20) NULL CONSTRAINT DF_Filmler_Durum DEFAULT 'Müsait',
            Image VARCHAR(255) NULL,
            Hakkinda NVARCHAR(MAX) NULL);
        CREATE TABLE dbo.Kiralama (
            KiralamaID INT IDENTITY(1,1) PRIMARY KEY,
            FilmID INT NULL,
            AlisTarihi DATE NOT NULL,
            IadeTarihi DATE NULL,
            Ucret DECIMAL(10, 2) NULL,
            KullaniciID INT NULL CONSTRAINT FK_Kiralama_KullaniciID REFERENCES dbo.Kullanicilar (KullaniciID));
        """;

    // Fictional rows. Identity values deliberately do not start at 1 so overwritten or re-seeded rows
    // would show. Every role spelling and film status the original application wrote is present.
    private const string LegacyData = """
        SET IDENTITY_INSERT dbo.Kullanicilar ON;
        INSERT dbo.Kullanicilar (KullaniciID, KullaniciAdi, Sifre, Rol, Eposta, Telefon) VALUES
            (3, N'yonetici', N'admin1', N'Admin', N'yonetici@example.com', '5550000101'),
            (5, N'uye', N'1234', N'Müşteri', N'uye@example.com', '555 000 0102'),
            (6, N'eski', N'abcd', N'Müsteri', NULL, NULL),
            (8, N'rolsuz', N'wxyz', NULL, N'', '');
        SET IDENTITY_INSERT dbo.Kullanicilar OFF;
        SET IDENTITY_INSERT dbo.Filmler ON;
        INSERT dbo.Filmler (FilmID, Ad, Tur, Yil, Yonetmen, GunlukUcret, Durum, Image, Hakkinda) VALUES
            (2, N'Eski Film', N'Dram', 1999, N'Ada Örnek', 12.50, N'Müsait', 'https://example.com/a.png', N'Açıklama'),
            (4, N'Kiradaki Film', N'Gerilim', 2005, N'Mert Taslak', 20.00, N'Kirada', NULL, NULL),
            (7, N'Gizlenen Film', N'Komedi', 2010, N'Ece Deneme', 9.50, N'Kirada', NULL, N'Elle kiraya verilmiş'),
            (9, N'Durumsuz Film', N'Komedi', 2011, N'Ece Deneme', 8.00, NULL, NULL, NULL);
        SET IDENTITY_INSERT dbo.Filmler OFF;
        SET IDENTITY_INSERT dbo.Kiralama ON;
        INSERT dbo.Kiralama (KiralamaID, FilmID, AlisTarihi, IadeTarihi, Ucret, KullaniciID) VALUES
            (3, 4, '2026-02-20', '2026-02-25', 100.00, 5);
        SET IDENTITY_INSERT dbo.Kiralama OFF;
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The migrations of the model; each is recorded once, also when two starts upgrade at the same time.</summary>
    private static int MigrationCount { get; } = new FilmDbContext(new DbContextOptionsBuilder<FilmDbContext>().UseSqlServer("Server=.").Options).Database.GetMigrations().Count();

    [Fact]
    public async Task Initialize_LegacyDatabase_KeepsTheRowsAndTurnsThemIntoTheNewModel()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);

        await using var provider = database.BuildServices();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(Ct);
        await initializer.InitializeAsync(Ct); // A second start must be a no-op.

        // Rows and identities are kept; no demonstration data was added.
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID IN (3, 5, 6, 8)", Ct));
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE FilmID IN (2, 4, 7, 9)", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));

        // Roles: exactly "Admin" stays an admin, both customer spellings and no role become users.
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 3 AND Rol COLLATE Latin1_General_BIN2 = N'Admin'", Ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID IN (5, 6, 8) AND Rol COLLATE Latin1_General_BIN2 = N'Kullanici'", Ct));
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi = 1 AND BasarisizGiris = 0 AND KilitBitis IS NULL", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 5 AND Telefon = '5550000102'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 8 AND Eposta IS NULL AND Telefon IS NULL", Ct));

        // One copy per film; the rented one is off the shelf; hidden films stay hidden.
        Assert.Equal(4, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE Adet = 1", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = 4", Ct));
        Assert.Equal(3, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE FilmID IN (2, 7, 9) AND MevcutAdet = 1", Ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE FilmID IN (2, 4) AND KiralamayaAcik = 1", Ct));
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE FilmID IN (7, 9) AND KiralamayaAcik = 0", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Filmler') AND name = N'Durum'", Ct));

        // The rental is active with its start and planned return day.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = 3 AND Durum = 0 AND KiralamaTarihi = '2026-02-20' AND PlanlananIadeTarihi = '2026-02-25' AND GercekIadeTarihi IS NULL AND GecikmeUcreti IS NULL",
            Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", Ct));

        await AssertSameSchemaAsFreshDatabaseAsync(database);

        // Plain text passwords still work once and are replaced with hashes; roles are kept.
        var auth = provider.GetRequiredService<IAuthService>();
        var admin = await auth.SignInAsync("yonetici", "admin1", Ct);
        var user = await auth.SignInAsync("uye", "1234", Ct);
        Assert.True(admin.IsAdmin);
        Assert.False(user.IsAdmin);
        Assert.False((await auth.SignInAsync("rolsuz", "wxyz", Ct)).IsAdmin);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 3 AND Sifre LIKE N'PBKDF2-SHA256$%' AND SifreBicimi = 0", Ct));
        await auth.SignInAsync("yonetici", "admin1", Ct);

        // The customer sees the old rental and returns it late; the copy goes back. The return day is the
        // database's (UTC, as the test clock's zone), not the test computer's clock.
        var rentals = provider.GetRequiredService<IRentalService>();
        var mine = Assert.Single(await rentals.GetMyActiveRentalsAsync(user, Ct));
        Assert.Equal(new DateOnly(2026, 2, 25), mine.DueOn);
        var returned = await rentals.ReturnAsync(user, mine.Id, Ct);
        var late = await database.ScalarAsync("SELECT DATEDIFF(DAY, '2026-02-25', CAST(SYSUTCDATETIME() AS date))", Ct);
        Assert.Equal(late, returned.OverdueDays);
        Assert.Equal(late * DailyLateFee, returned.LateFee);
        Assert.Equal(1, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = 4", Ct));

        // Listed films only; new rows continue after the existing identities.
        var catalog = await provider.GetRequiredService<IFilmService>().GetCatalogAsync(user, FilmFilter.None, Ct);
        Assert.Equal([2, 4], catalog.Select(f => f.Id));
        var receipt = await RentAsync(provider, user, 2);
        Assert.True(receipt.RentalId > 3);
        var filmId = await AddFilmAsync(provider, "Yeni Film", 2, admin: admin);
        Assert.True(filmId > 9);

        // The tables added after the original schema work on the upgraded database: the customer rates
        // the old film just returned and puts another one on the watchlist.
        await provider.GetRequiredService<FilmKiralama.Business.Reviews.IReviewService>().SaveAsync(user, 4, new FilmKiralama.Business.Reviews.ReviewInput(4, "Eski ama iyi"), Ct);
        Assert.True(await provider.GetRequiredService<FilmKiralama.Business.Watchlists.IWatchlistService>().AddAsync(user, filmId, Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Degerlendirmeler WHERE FilmID = 4 AND KullaniciID = 5 AND Puan = 4", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.IzlemeListesi WHERE KullaniciID = 5", Ct));

        // Rows the upgrade kept with NULLs can be saved from the admin forms as they are: the password of
        // an account without e-mail and phone is reset, a film without a description is listed again.
        await provider.GetRequiredService<IAccountService>().UpdateLoadedAsync(admin, 6, new AccountInput("eski", "Yeni.Sifre12", "", "", UserRole.User), Ct);
        Assert.Equal(6, (await auth.SignInAsync("eski", "Yeni.Sifre12", Ct)).UserId);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciID = 6 AND Eposta IS NULL AND Telefon IS NULL", Ct));
        var filmService = provider.GetRequiredService<IFilmService>();
        var hidden = await filmService.GetAsync(admin, 9, Ct);
        await filmService.UpdateLoadedAsync(
            admin,
            9,
            new FilmInput(hidden.Title, hidden.Genre, "2011", hidden.Director, "8", hidden.ImageUrl, hidden.About, hidden.TotalCopies, IsListed: true),
            Ct);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE FilmID = 9 AND KiralamayaAcik = 1 AND Hakkinda IS NULL", Ct));
    }

    [Fact]
    public async Task Initialize_RowsOfUncertainMeaning_RefuseTheUpgradeWithCountsAndChangeNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await database.ExecuteAsync(
            """
            INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, Rol) VALUES (N'kucuk', N'x', N'admin'), (N'bosluklu', N'y', N'Admin ');
            INSERT dbo.Filmler (Ad, Tur, Yil, Yonetmen, GunlukUcret, Durum) VALUES (N'Bakimdaki', N'Dram', 2000, N'Ada', 5, N'Bakımda');
            INSERT dbo.Kiralama (FilmID, AlisTarihi, IadeTarihi, Ucret, KullaniciID) VALUES (2, '2026-02-01', '2026-02-03', 25, 6), (4, '2026-02-01', '2026-02-03', 40, 6), (77, '2026-02-01', NULL, 10, 6);
            """,
            Ct);
        var usersBefore = await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.Kullanicilar", Ct);
        var filmsBefore = await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(FilmID, Ad, Durum)) FROM dbo.Filmler", Ct);

        await using var provider = database.BuildServices();
        var error = await Assert.ThrowsAsync<DatabaseInitializationException>(() => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));

        var message = error.InnerException!.Message;
        Assert.Contains("2 users with a missing or too long name, password, e-mail or phone, or an unknown role", message, StringComparison.Ordinal);
        Assert.Contains("1 films whose status is neither", message, StringComparison.Ordinal);
        Assert.Contains("1 films marked 'Müsait' that are rented", message, StringComparison.Ordinal);
        Assert.Contains("1 films with more than one rental at the same time", message, StringComparison.Ordinal);
        Assert.Contains("1 rentals without an existing film or account", message, StringComparison.Ordinal);
        Assert.Contains("No changes were made", message, StringComparison.Ordinal);
        Assert.DoesNotContain("kucuk", message, StringComparison.Ordinal);

        Assert.Equal(usersBefore, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(filmsBefore, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(FilmID, Ad, Durum)) FROM dbo.Filmler", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN (N'__EFMigrationsHistory', N'KurulumAnahtarlari')", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Filmler') AND name = N'Adet'", Ct));
    }

    [Fact]
    public async Task Initialize_TwoInstancesAtOnce_UpgradeOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await using var a = database.BuildServices();
        await using var b = database.BuildServices();

        await Task.WhenAll(
            Task.Run(() => a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct),
            Task.Run(() => b.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct));

        Assert.Equal(MigrationCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = 4", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
    }

    /// <summary>
    /// While another start holds the upgrade lock, a second start waits for it instead of failing,
    /// even with a command timeout shorter than the wait, and then finds the work to do.
    /// </summary>
    [Fact]
    public async Task Initialize_WhileAnotherStartHoldsTheUpgradeLock_WaitsInsteadOfFailing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await using var provider = database.BuildServicesWithCommandTimeout(5);

        Task initialization;
        await using (await database.HoldAppLockAsync("FilmKiralama.LegacySchemaUpgrade", Ct))
        {
            initialization = Task.Run(() => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct), Ct);
            await Task.Delay(TimeSpan.FromSeconds(9), Ct);
            Assert.False(initialization.IsCompleted, "The second start did not wait for the upgrade lock.");
        }

        await initialization;
        Assert.Equal(MigrationCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = 4", Ct));
    }

    /// <summary>A start that gives up waiting for the upgrade lock reports the refused lock, so it can say "busy".</summary>
    [Fact]
    public async Task Initialize_UpgradeLockHeldLongerThanTheWait_ReportsTheRefusedLock()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacySchema, Ct);
        await database.ExecuteAsync(LegacyData, Ct);
        await using var provider = database.BuildServicesWithCommandTimeout(5);

        await using (await database.HoldAppLockAsync("FilmKiralama.LegacySchemaUpgrade", Ct))
        {
            var error = await Assert.ThrowsAsync<DatabaseInitializationException>(() => provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));
            Assert.IsType<AppLockNotGrantedException>(error.InnerException);
        }

        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = N'__EFMigrationsHistory'", Ct));
    }

    [Fact]
    public async Task Initialize_NewDatabase_HasNoAccountAndNoFilm()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices();

        Assert.Equal(DemoSeedOutcome.NotRequested, await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct));

        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => provider.GetRequiredService<IAuthService>().SignInAsync("admin", "admin", Ct));
    }

    /// <summary>
    /// Restores a backup of the original application's database (path in FILM_LEGACY_BACKUP, readable by
    /// the SQL Server service) and upgrades it. Only counts, identities and checksums are compared, so no
    /// personal data from the backup ends up in test output.
    /// </summary>
    [Fact]
    public async Task Initialize_RestoredLegacyBackup_KeepsAllRows()
    {
        var backup = Environment.GetEnvironmentVariable(BackupEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(backup))
        {
            Assert.Skip($"{BackupEnvironmentVariable} is not set.");
        }

        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await RestoreAsync(database, backup, Ct);

        const string Users = "SELECT CHECKSUM_AGG(CHECKSUM(KullaniciID, KullaniciAdi, Sifre)) FROM dbo.Kullanicilar";
        const string Films = "SELECT CHECKSUM_AGG(CHECKSUM(FilmID, Ad, Tur, Yil, Yonetmen, GunlukUcret, Image)) FROM dbo.Filmler";
        const string Admins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol COLLATE Latin1_General_BIN2 = N'Admin'";
        var usersBefore = await database.ScalarAsync(Users, Ct);
        var filmsBefore = await database.ScalarAsync(Films, Ct);
        var adminsBefore = await database.ScalarAsync(Admins, Ct);
        var userCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct);
        var filmCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler", Ct);
        var rentalCount = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct);
        var rentals = await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(KiralamaID, FilmID, KullaniciID, AlisTarihi, IadeTarihi, Ucret)) FROM dbo.Kiralama", Ct);
        var visibleBefore = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE Durum != 'Kirada'", Ct);

        await using var provider = database.BuildServices();
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        Assert.True(rentalCount > 0, "The backup has no rentals.");
        Assert.Equal(userCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", Ct));
        Assert.Equal(filmCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler", Ct));
        Assert.Equal(usersBefore, await database.ScalarAsync(Users, Ct));
        Assert.Equal(filmsBefore, await database.ScalarAsync(Films, Ct));
        Assert.Equal(adminsBefore, await database.ScalarAsync(Admins, Ct));
        Assert.Equal(rentals, await database.ScalarAsync("SELECT CHECKSUM_AGG(CHECKSUM(KiralamaID, FilmID, KullaniciID, KiralamaTarihi, PlanlananIadeTarihi, Ucret)) FROM dbo.Kiralama", Ct));
        Assert.Equal(rentalCount, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE Durum = 0", Ct));
        Assert.Equal(filmCount - rentalCount, await database.ScalarAsync("SELECT SUM(MevcutAdet) FROM dbo.Filmler", Ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE SifreBicimi <> 1", Ct));

        // The customers see the same films as before: available and not hidden by hand.
        var customer = User(1);
        var catalog = await provider.GetRequiredService<IFilmService>().GetCatalogAsync(customer, FilmFilter.None, Ct);
        Assert.Equal(visibleBefore, catalog.Count);
        await AssertSameSchemaAsFreshDatabaseAsync(database);
    }

    /// <summary>
    /// Compares every table, column, key, check, default and index of <paramref name="upgraded"/> with
    /// a database created by the migrations, so the upgrade leaves no difference behind.
    /// </summary>
    private static async Task AssertSameSchemaAsFreshDatabaseAsync(SqlServerTestDatabase upgraded)
    {
        await using var fresh = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using (var provider = fresh.BuildServices())
        {
            await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        }

        foreach (var query in SchemaQueries)
        {
            var expected = await fresh.TextAsync(query, Ct);
            Assert.False(string.IsNullOrEmpty(expected));
            Assert.Equal(expected, await upgraded.TextAsync(query, Ct));
        }
    }

    private static readonly string[] SchemaQueries =
    [
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(t.name, '.', c.name, ' ', ty.name, ' ', c.max_length, ' ', c.precision, ' ', c.scale, ' ', c.is_nullable, ' ', c.is_identity)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY t.name, c.name)
        FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE t.name <> N'__EFMigrationsHistory'
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(o.name, ' ', o.type, ' ', OBJECT_NAME(o.parent_object_id), ' ', cc.definition, dc.definition)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY o.name)
        FROM sys.objects o
        LEFT JOIN sys.check_constraints cc ON cc.object_id = o.object_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = o.object_id
        WHERE o.type IN ('PK', 'F', 'C', 'D', 'UQ') AND OBJECT_NAME(o.parent_object_id) <> N'__EFMigrationsHistory'
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(f.name, ' ', OBJECT_NAME(f.parent_object_id), '.', COL_NAME(fc.parent_object_id, fc.parent_column_id), ' > ', OBJECT_NAME(f.referenced_object_id), '.', COL_NAME(fc.referenced_object_id, fc.referenced_column_id), ' ', f.delete_referential_action)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY f.name)
        FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id = f.object_id
        """,
        """
        SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(x.TableName, '.', x.IndexName, ' ', x.is_unique, ' ', x.filter_definition, ' ', x.Columns)) COLLATE Latin1_General_BIN2, N'|')
            WITHIN GROUP (ORDER BY x.TableName, x.IndexName)
        FROM (
            SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.filter_definition,
                (SELECT STRING_AGG(COL_NAME(ic.object_id, ic.column_id) COLLATE Latin1_General_BIN2, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) AS Columns
            FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.name IS NOT NULL AND t.name <> N'__EFMigrationsHistory') x
        """,
    ];

    private static async Task RestoreAsync(SqlServerTestDatabase database, string backup, CancellationToken ct)
    {
        await SqlServerTestDatabase.ExecuteAsync(
            database.Server,
            """
            DECLARE @files TABLE (
                LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0),
                MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier,
                ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int,
                LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit,
                IsPresent bit, TDEThumbprint varbinary(32), SnapshotUrl nvarchar(360));
            INSERT @files EXEC sp_executesql N'RESTORE FILELISTONLY FROM DISK = @p', N'@p nvarchar(4000)', @p = @backup;
            DECLARE @data nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'D');
            DECLARE @log nvarchar(128) = (SELECT LogicalName FROM @files WHERE Type = 'L');
            DECLARE @path nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));
            DECLARE @mdf nvarchar(500) = @path + @name + N'.mdf';
            DECLARE @ldf nvarchar(500) = @path + @name + N'_log.ldf';
            RESTORE DATABASE @name FROM DISK = @backup WITH MOVE @data TO @mdf, MOVE @log TO @ldf, RECOVERY;
            """,
            ct,
            ("@name", database.Name),
            ("@backup", backup));
    }
}
