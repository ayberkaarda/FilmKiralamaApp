using FilmKiralama.Business;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// Races on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>), each side with its own
/// services and connection pool, as two application instances: customers race for the last copy, one
/// customer races itself, a rental is returned twice at once, failed sign-ins arrive together and two
/// admins demote each other.
/// </summary>
public sealed class SqlServerConcurrencyTests
{
    private const int Rounds = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rent_TwoCustomersRaceForTheLastCopy_ExactlyOneGetsIt()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var first = await RegisterAsync(a, "birinci", 1);
        var second = await RegisterAsync(a, "ikinci", 2);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Son Kopya " + round, copies: 1);

            var results = await RaceAsync(
                [() => RentAsync(a, first, filmId), () => RentAsync(b, second, filmId)]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.NotAvailable, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            await AssertStockAsync(database, filmId, active: 1, available: 0);
        }
    }

    [Fact]
    public async Task Rent_ManyCustomersRaceForTwoCopies_StockNeverGoesBelowZero()
    {
        const int Customers = 12;
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var sessions = new List<UserSession>();
        for (var i = 0; i < Customers; i++)
        {
            sessions.Add(await RegisterAsync(a, "musteri" + i, 100 + i));
        }

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Iki Kopya " + round, copies: 2);

            var results = await RaceAsync(sessions.Select((s, i) => (Func<Task>)(() => RentAsync(i % 2 == 0 ? a : b, s, filmId))).ToList());

            Assert.Equal(2, results.Count(r => r is null));
            Assert.All(results.Where(r => r is not null), r => Assert.Equal(RentalService.NotAvailable, Assert.IsType<BusinessRuleException>(r).Message));
            await AssertStockAsync(database, filmId, active: 2, available: 0);
        }
    }

    [Fact]
    public async Task Rent_OneCustomerRacesItself_HasOneActiveRental()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "tekrar", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Tekrar " + round, copies: 5);

            var results = await RaceAsync(Enumerable.Range(0, 8).Select(i => (Func<Task>)(() => RentAsync(i % 2 == 0 ? a : b, customer, filmId))).ToList());

            Assert.Single(results, r => r is null);
            Assert.All(results.Where(r => r is not null), r => Assert.Equal(RentalService.AlreadyRented, Assert.IsType<BusinessRuleException>(r).Message));
            await AssertStockAsync(database, filmId, active: 1, available: 4);
        }
    }

    [Fact]
    public async Task Return_SameRentalTwiceAtOnce_PutsOneCopyBack()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "iade", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Iade " + round, copies: 1);
            var receipt = await RentAsync(a, customer, filmId);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IRentalService>().ReturnAsync(customer, receipt.RentalId, Ct),
                () => b.GetRequiredService<IRentalService>().ReturnAsync(Admin(SeedAdminId), receipt.RentalId, Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(RentalService.RentalNotFound, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            await AssertStockAsync(database, filmId, active: 0, available: 1);
        }
    }

    [Fact]
    public async Task SignIn_ConcurrentFailures_AreAllCountedAndLockTheAccount()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        await RegisterAsync(a, "kilit", 1);

        await RaceAsync(Enumerable.Range(0, 5).Select(i => (Func<Task>)(() => (i % 2 == 0 ? a : b).GetRequiredService<IAuthService>().SignInAsync("kilit", "Yanlis.Sifre1", Ct))).ToList());

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => a.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, error.Message);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND KilitBitis IS NOT NULL AND BasarisizGiris = 0", Ct));
    }

    /// <summary>
    /// Two wrong passwords for an account with four failures, both read while it was unlocked and held
    /// between the password check and the write. The first one locks the account; the second one arrives
    /// while the lock (by the database's clock) is in force and counts nothing: the counter stays at zero
    /// and the lock is neither extended nor shortened.
    /// </summary>
    [Fact]
    public async Task SignIn_FailureInFlightWhenTheAccountLocks_CountsNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        var hasher = new GatedHasher();
        await using var a = database.BuildServices(services => services.AddSingleton<IPasswordHasher>(hasher), demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await RegisterAsync(a, "ucustaki", 1);
        var auth = a.GetRequiredService<IAuthService>();
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ucustaki", "Yanlis.Sifre1", Ct));
        }

        var first = hasher.Gate();
        var second = hasher.Gate();
        var locking = Task.Run(() => auth.SignInAsync("ucustaki", "Yanlis.Sifre1", Ct), Ct);
        await first.Entered.WaitAsync(Ct);
        var late = Task.Run(() => auth.SignInAsync("ucustaki", "Yanlis.Sifre1", Ct), Ct);
        await second.Entered.WaitAsync(Ct);

        first.Release.SetResult();
        await Assert.ThrowsAsync<BusinessRuleException>(() => locking);
        var lockEnd = await database.TextAsync("SELECT CONVERT(nvarchar(40), KilitBitis, 126) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'ucustaki'", Ct);
        Assert.NotNull(lockEnd);

        second.Release.SetResult();
        await Assert.ThrowsAsync<BusinessRuleException>(() => late);
        Assert.Equal(0, await database.ScalarAsync("SELECT BasarisizGiris FROM dbo.Kullanicilar WHERE KullaniciAdi = N'ucustaki'", Ct));
        Assert.Equal(lockEnd, await database.TextAsync("SELECT CONVERT(nvarchar(40), KilitBitis, 126) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'ucustaki'", Ct));
    }

    /// <summary>
    /// A wrong password read before an admin set a new password (which also lifts any lock) and written
    /// after it counts nothing against the new password: the failure belonged to the old credential and
    /// the old session version.
    /// </summary>
    [Fact]
    public async Task SignIn_FailureInFlightWhenThePasswordIsReset_CountsNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        var hasher = new GatedHasher();
        await using var a = database.BuildServices(services => services.AddSingleton<IPasswordHasher>(hasher), demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var customer = await RegisterAsync(a, "sifirlanan", 1);
        var auth = a.GetRequiredService<IAuthService>();
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("sifirlanan", "Yanlis.Sifre1", Ct));
        }

        var gate = hasher.Gate();
        var stale = Task.Run(() => auth.SignInAsync("sifirlanan", "Yanlis.Sifre1", Ct), Ct);
        await gate.Entered.WaitAsync(Ct);
        await a.GetRequiredService<IAccountService>().UpdateLoadedAsync(
            Admin(SeedAdminId),
            customer.UserId,
            new AccountInput("sifirlanan", "Yeni.Sifre123", "sifirlanan@example.com", "5540000001", UserRole.User),
            Ct);

        gate.Release.SetResult();
        await Assert.ThrowsAsync<BusinessRuleException>(() => stale);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'sifirlanan' AND BasarisizGiris = 0 AND KilitBitis IS NULL", Ct));
        Assert.Equal(customer.UserId, (await auth.SignInAsync("sifirlanan", "Yeni.Sifre123", Ct)).UserId);
    }

    [Fact]
    public async Task UpdateAccount_TwoAdminsDemoteEachOther_OneAdminRemains()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();

        for (var round = 0; round < Rounds; round++)
        {
            var accounts = a.GetRequiredService<IAccountService>();
            var x = await accounts.AddAsync(await CurrentSessionAsync(a, SeedAdminId), Input("xadmin" + round, 200 + round, UserRole.Admin), Ct);
            var y = await accounts.AddAsync(await CurrentSessionAsync(a, SeedAdminId), Input("yadmin" + round, 300 + round, UserRole.Admin), Ct);

            // The seed admin steps down first, so x and y are the only admins.
            await accounts.UpdateLoadedAsync(Admin(x), SeedAdminId, new AccountInput("yonetici", null, "yonetici@example.com", "5550000001", UserRole.User), Ct);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<IAccountService>().UpdateLoadedAsync(Admin(x), y, Input("yadmin" + round, 300 + round, UserRole.User), Ct),
                () => b.GetRequiredService<IAccountService>().UpdateLoadedAsync(Admin(y), x, Input("xadmin" + round, 200 + round, UserRole.User), Ct),
            ]);

            // The second one to get the admin role lock has lost the role by then, so its open session
            // is refused as revoked; it can neither demote the winner nor keep the role.
            Assert.Single(results, r => r is null);
            Assert.Equal(AccessDeniedException.SessionRevokedMessage, Assert.IsType<AccessDeniedException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));

            // Hand the role back to the seed admin for the next round.
            var remaining = await database.ScalarAsync("SELECT KullaniciID FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct);
            await accounts.UpdateLoadedAsync(await CurrentSessionAsync(a, remaining), SeedAdminId, new AccountInput("yonetici", null, "yonetici@example.com", "5550000001", UserRole.Admin), Ct);
            await accounts.UpdateLoadedAsync(await CurrentSessionAsync(a, SeedAdminId), remaining, remaining == x ? Input("xadmin" + round, 200 + round, UserRole.User) : Input("yadmin" + round, 300 + round, UserRole.User), Ct);
        }
    }

    /// <summary>
    /// A film lock held by another instance makes a rental wait for the whole lock timeout and then
    /// end as "busy" (the lock was refused), never as a command timeout, even when the configured
    /// command timeout is shorter than the lock wait. Once the lock is free the rental works.
    /// </summary>
    [Fact]
    public async Task HeldFilmLock_IsReportedAsBusyNotAsACommandTimeout()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServicesWithCommandTimeout(5, demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        var waited = System.Diagnostics.Stopwatch.StartNew();
        await using (await database.HoldAppLockAsync("FilmKiralama.Film." + ThreeCopiesFilmId, Ct))
        {
            await Assert.ThrowsAsync<AppLockNotGrantedException>(() => RentAsync(a, User(SeedUserId), ThreeCopiesFilmId));
        }

        Assert.True(waited.Elapsed >= TimeSpan.FromSeconds(25), "The rental did not wait for the film lock.");
        await RentAsync(a, User(SeedUserId), ThreeCopiesFilmId);
        await AssertStockAsync(database, ThreeCopiesFilmId, active: 1, available: 2);
    }

    /// <summary>
    /// The stored account decides on SQL Server too (the locking read of the actor's row): a demoted
    /// admin's open session is refused everywhere, so is the session of an admin whose password another
    /// admin reset (the role stayed), and a deleted customer's session cannot rent.
    /// </summary>
    [Fact]
    public async Task RevokedSessions_AreRefusedAndChangeNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var accounts = a.GetRequiredService<IAccountService>();
        var second = Input("ikinci", 1, UserRole.Admin);
        var secondId = await accounts.AddAsync(Admin(SeedAdminId), second, Ct);
        var demoted = await b.GetRequiredService<IAuthService>().SignInAsync("ikinci", Password, Ct);
        var third = Input("ucuncu", 3, UserRole.Admin);
        var thirdId = await accounts.AddAsync(Admin(SeedAdminId), third, Ct);
        var reset = await b.GetRequiredService<IAuthService>().SignInAsync("ucuncu", Password, Ct);
        var customer = await RegisterAsync(a, "giden", 2);
        var rental = await RentAsync(a, User(SeedUserId), ThreeCopiesFilmId);

        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), secondId, second with { Password = null, Role = UserRole.User }, Ct);
        await accounts.DeleteAsync(Admin(SeedAdminId), customer.UserId, Ct);
        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), thirdId, third with { Password = "Yeni.Sifre123" }, Ct);

        var films = b.GetRequiredService<IFilmService>();
        var rentals = b.GetRequiredService<IRentalService>();
        List<Func<Task>> refused =
        [
            () => b.GetRequiredService<IAccountService>().UpdateLoadedAsync(demoted, secondId, second with { Password = null }, Ct),
            () => b.GetRequiredService<IAccountService>().GetAllAsync(demoted, Ct),
            () => films.AddAsync(demoted, new FilmInput("Yeni", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 1, true), Ct),
            () => films.DeleteAsync(demoted, ThreeCopiesFilmId, Ct),
            () => rentals.ReturnAsync(demoted, rental.RentalId, Ct),
            async () => await rentals.RentAsync(customer, await rentals.QuoteAsync(ThreeCopiesFilmId, 25m, 1, Ct), Ct),
            () => films.AddAsync(reset, new FilmInput("Yeni", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 1, true), Ct),
            () => b.GetRequiredService<IAccountService>().UpdateLoadedAsync(reset, thirdId, third, Ct),
        ];
        foreach (var operation in refused)
        {
            Assert.Equal(AccessDeniedException.SessionRevokedMessage, (await Assert.ThrowsAsync<AccessDeniedException>(operation)).Message);
        }

        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'", Ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT OturumSurumu FROM dbo.Kullanicilar WHERE KullaniciID = @id", Ct, ("@id", thirdId)));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Filmler WHERE Ad = N'Yeni'", Ct));
        await AssertStockAsync(database, ThreeCopiesFilmId, active: 1, available: 2);
    }

    /// <summary>
    /// The first review of a customer saved twice at the same moment from two instances: both saves
    /// succeed and one review remains, carrying one of the two ratings.
    /// </summary>
    [Fact]
    public async Task Review_SavedTwiceAtOnce_LeavesOneReview()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "yorumcu", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Yorum " + round, copies: 1);
            var rental = await RentAsync(a, customer, filmId);
            await a.GetRequiredService<IRentalService>().ReturnAsync(customer, rental.RentalId, Ct);

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<FilmKiralama.Business.Reviews.IReviewService>().SaveAsync(customer, filmId, new FilmKiralama.Business.Reviews.ReviewInput(2, "a"), Ct),
                () => b.GetRequiredService<FilmKiralama.Business.Reviews.IReviewService>().SaveAsync(customer, filmId, new FilmKiralama.Business.Reviews.ReviewInput(5, "b"), Ct),
            ]);

            Assert.All(results, Assert.Null);
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Degerlendirmeler WHERE FilmID = @id AND Puan IN (2, 5)", Ct, ("@id", filmId)));
        }
    }

    /// <summary>
    /// The same film added twice at the same moment from two instances: both adds succeed (one adds it,
    /// one finds it there) and the list holds it once.
    /// </summary>
    [Fact]
    public async Task Watchlist_SameFilmAddedTwiceAtOnce_IsListedOnce()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var customer = await RegisterAsync(a, "liste", 1);

        for (var round = 0; round < Rounds; round++)
        {
            var filmId = await AddFilmAsync(a, "Liste " + round, copies: 1);
            var added = new bool[2];

            var results = await RaceAsync(
            [
                async () => added[0] = await a.GetRequiredService<FilmKiralama.Business.Watchlists.IWatchlistService>().AddAsync(customer, filmId, Ct),
                async () => added[1] = await b.GetRequiredService<FilmKiralama.Business.Watchlists.IWatchlistService>().AddAsync(customer, filmId, Ct),
            ]);

            Assert.All(results, Assert.Null);
            Assert.Single(added, x => x);
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.IzlemeListesi WHERE FilmID = @id", Ct, ("@id", filmId)));
        }
    }

    /// <summary>One place left on a list and two different films added at once: one fits, the other is refused.</summary>
    [Fact]
    public async Task Watchlist_LastPlaceTakenTwiceAtOnce_HoldsTheLimit()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await using var b = database.BuildServices();
        var limit = FilmKiralama.Business.Watchlists.WatchlistPolicy.MaxEntries;
        await database.ExecuteAsync(
            """
            INSERT dbo.Filmler (Ad, Tur, Yil, Yonetmen, GunlukUcret, Adet, MevcutAdet, KiralamayaAcik)
                SELECT TOP (@count) CONCAT(N'Dolu ', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))), N'Dram', 2020, N'Ece', 5, 1, 1, 1 FROM sys.all_objects;
            """,
            Ct,
            ("@count", limit + 1));
        var first = await database.ScalarAsync("SELECT MAX(FilmID) FROM dbo.Filmler WHERE Ad LIKE N'Dolu %'", Ct);
        var second = first - 1;

        for (var round = 0; round < Rounds; round++)
        {
            var customer = await RegisterAsync(a, "dolu" + round, 10 + round);
            await database.ExecuteAsync(
                "INSERT dbo.IzlemeListesi (KullaniciID, FilmID, EklenmeTarihi) SELECT TOP (@count) @user, FilmID, SYSUTCDATETIME() FROM dbo.Filmler WHERE Ad LIKE N'Dolu %' AND FilmID < @second ORDER BY FilmID",
                Ct,
                ("@count", limit - 1),
                ("@user", customer.UserId),
                ("@second", second));

            var results = await RaceAsync(
            [
                () => a.GetRequiredService<FilmKiralama.Business.Watchlists.IWatchlistService>().AddAsync(customer, first, Ct),
                () => b.GetRequiredService<FilmKiralama.Business.Watchlists.IWatchlistService>().AddAsync(customer, second, Ct),
            ]);

            Assert.Single(results, r => r is null);
            Assert.Equal(FilmKiralama.Business.Watchlists.WatchlistService.ListFull, Assert.IsType<BusinessRuleException>(Assert.Single(results, r => r is not null)).Message);
            Assert.Equal(limit, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.IzlemeListesi WHERE KullaniciID = @user", Ct, ("@user", customer.UserId)));
        }
    }

    /// <summary>
    /// The test's password hasher: each <see cref="Gate"/> holds one later password check, after the
    /// check and before the sign-in writes its outcome, until the test releases it.
    /// </summary>
    private sealed class GatedHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new(iterations: 1_000);
        private readonly System.Collections.Concurrent.ConcurrentQueue<(TaskCompletionSource Entered, TaskCompletionSource Release)> _gates = new();

        public (Task Entered, TaskCompletionSource Release) Gate()
        {
            var gate = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _gates.Enqueue(gate);
            return (gate.Item1.Task, gate.Item2);
        }

        public string Hash(string password) => _inner.Hash(password);

        public PasswordVerificationResult Verify(string password, string stored, PasswordFormat format)
        {
            var result = _inner.Verify(password, stored, format);
            if (_gates.TryDequeue(out var gate))
            {
                gate.Entered.SetResult();
                gate.Release.Task.Wait(Ct);
            }

            return result;
        }

        public void SimulateVerification(string password) => _inner.SimulateVerification(password);
    }

    private static AccountInput Input(string name, int number, UserRole role) =>
        new(name, Password, name + "@example.com", "553" + number.ToString("0000000", System.Globalization.CultureInfo.InvariantCulture), role);

    private static async Task AssertStockAsync(SqlServerTestDatabase database, int filmId, int active, int available)
    {
        Assert.Equal(active, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama WHERE FilmID = @id AND Durum = 0", Ct, ("@id", filmId)));
        Assert.Equal(available, await database.ScalarAsync("SELECT MevcutAdet FROM dbo.Filmler WHERE FilmID = @id", Ct, ("@id", filmId)));
    }

    /// <summary>
    /// Starts every operation at the same moment and returns, per operation, the exception it ended with
    /// or <see langword="null"/>. A database error other than a refusal fails the test.
    /// </summary>
    private static async Task<IReadOnlyList<Exception?>> RaceAsync(IReadOnlyList<Func<Task>> operations)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = operations
            .Select(operation => Task.Run(
                async () =>
                {
                    start.Wait(Ct);
                    try
                    {
                        await operation();
                        return (Exception?)null;
                    }
                    catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException)
                    {
                        return ex;
                    }
                },
                Ct))
            .ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);
        Assert.DoesNotContain(results, r => r is SqlException);
        return results;
    }
}
