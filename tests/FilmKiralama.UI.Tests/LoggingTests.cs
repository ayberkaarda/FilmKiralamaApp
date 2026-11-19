using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FilmKiralama.Business;
using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Data;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FilmKiralama.UI.Tests;

/// <summary>
/// What reaches the log file. The services run for real on a SQLite in-memory database; no window or
/// message box is created by these tests.
/// </summary>
public sealed partial class LoggingTests : IDisposable
{
    // Personal data and secrets as they appear in sign-ups, sign-ins, films and rentals.
    private const string CustomerName = "deniz.ornekoglu";
    private const string CustomerEmail = "deniz.ornek@example.com";
    private const string CustomerPhone = "5551234567";
    private const string CustomerPassword = "Gizli.Musteri1";
    private const string AdminName = "ada.kurucuoglu";
    private const string AdminPassword = "Gizli.Yonetici1";
    private const string FilmTitle = "Kuantum Sineması";
    private const string Director = "Zeynep Yönetmenoğlu";
    private const string About = "Gizli bir laboratuvarda geçen hikâye.";
    private const string Poster = "https://afis.example.com/kuantum.png";

    // Wednesday 5 March 2031, 10:10 UTC: the year cannot appear in a log timestamp written today.
    private static readonly DateTimeOffset Now = new(2031, 3, 5, 10, 10, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "filmkiralama-log-test-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void CreateLogger_WritesADailyFileInTheGivenDirectory()
    {
        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches(@"filmkiralama-\d{8}\.log$", file);
        Assert.Contains("probe", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalApplicationData()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(root, AppLogging.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("FilmKiralama", "logs"), AppLogging.DefaultDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Files dated before the last 30 days are deleted when the logger opens today's file.</summary>
    [Fact]
    public void Retention_DeletesFilesOlderThanThirtyDaysAndKeepsRecentOnes()
    {
        int[] ages = [1, 2, 15, 29, 30, 31, 45, 60, 400];
        Directory.CreateDirectory(_directory);
        foreach (var age in ages)
        {
            File.WriteAllText(DatedFile(age), "old");
        }

        var unrelated = Path.Combine(_directory, "notlar.txt");
        File.WriteAllText(unrelated, "not a log file");

        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        Assert.Equal(30, AppLogging.RetainedDays);
        Assert.All(ages.Where(a => a < 30), age => Assert.True(File.Exists(DatedFile(age)), $"The file of {age} days ago was deleted."));
        Assert.All(ages.Where(a => a >= 30), age => Assert.False(File.Exists(DatedFile(age)), $"The file of {age} days ago was kept."));
        Assert.True(File.Exists(DatedFile(0)));
        Assert.True(File.Exists(unrelated));
    }

    /// <summary>
    /// More than 30 recent files (several per day, as size or restart sequences produce) are cut to the
    /// newest 30, today's file included.
    /// </summary>
    [Fact]
    public void Retention_KeepsAtMostThirtyFilesNewestFirst()
    {
        Directory.CreateDirectory(_directory);
        var created = new List<string>();
        for (var age = 1; age <= 10; age++)
        {
            created.Add(DatedFile(age));
            for (var sequence = 1; sequence <= 3; sequence++)
            {
                created.Add(DatedFile(age, sequence));
            }
        }

        foreach (var file in created)
        {
            File.WriteAllText(file, "old");
        }

        using (var logger = AppLogging.CreateLogger(_directory))
        {
            logger.Information("probe");
        }

        // Newest first: day 1 sequence 3, 2, 1, base, then day 2 and so on.
        var newestFirst = created.OrderBy(AgeOf).ThenByDescending(SequenceOf).ToList();
        var remaining = Directory.GetFiles(_directory, AppLogging.FilePrefix + "*.log");
        Assert.Equal(30, AppLogging.RetainedFileCount);
        Assert.Equal(AppLogging.RetainedFileCount, remaining.Length);
        Assert.All(newestFirst.Take(AppLogging.RetainedFileCount - 1), f => Assert.True(File.Exists(f), Path.GetFileName(f) + " was deleted."));
        Assert.All(newestFirst.Skip(AppLogging.RetainedFileCount - 1), f => Assert.False(File.Exists(f), Path.GetFileName(f) + " was kept."));
    }

    [Fact]
    public void Record_UnexpectedError_KeepsTypesCodesAndMethodsButNoMessageOrPersonalData()
    {
        var exception = Throw(new InvalidOperationException(
            $"Kayıt yazılamadı: {CustomerName} {CustomerEmail} {CustomerPhone} şifre {CustomerPassword}",
            new TimeoutException($"Server=gizli-sunucu;Password={AdminPassword} {FilmTitle}")));

        var text = WriteAndRead(logger => ErrorReporter.Record(logger, exception));

        Assert.Contains("[ERR] Operation failed", text, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
        Assert.Contains(typeof(TimeoutException).FullName!, text, StringComparison.Ordinal);
        Assert.Contains("HResult 0x", text, StringComparison.Ordinal);
        Assert.Contains(nameof(LoggingTests) + "." + nameof(Throw), text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Kayıt yazılamadı", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gizli-sunucu", text, StringComparison.Ordinal);
        AssertNoPersonalData(text);
    }

    /// <summary>Refusals are answers for the screen: they leave no entry, whatever their message holds.</summary>
    [Fact]
    public void Record_RefusedOperation_WritesNothing()
    {
        using (var logger = AppLogging.CreateLogger(_directory))
        {
            ErrorReporter.Record(logger, new BusinessRuleException($"{CustomerName} {CustomerEmail}"));
            ErrorReporter.Record(logger, new AccessDeniedException());
            logger.Information("probe");
        }

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        Assert.Equal(["[INF] probe"], Lines(text).Select(WithoutStamp).ToArray());
    }

    [Fact]
    public void AuditLog_WritesOnlyTheCodeAndTheRole()
    {
        var text = WriteAndRead(logger =>
        {
            var audit = new SerilogAuditLog(logger);
            foreach (var auditEvent in Enum.GetValues<AuditEvent>())
            {
                foreach (var role in Enum.GetValues<AuditRole>())
                {
                    audit.Record(auditEvent, role);
                }
            }
        });

        Assert.Contains("[INF] Audit auth.sign_in.failed unknown", text, StringComparison.Ordinal);
        Assert.Contains("[INF] Audit rental.created user", text, StringComparison.Ordinal);
        Assert.Contains("[INF] Audit film.changed admin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("auth.account.registered", text, StringComparison.Ordinal);
        Assert.DoesNotContain("registration_not_completed", text, StringComparison.Ordinal);
        Assert.Equal(Enum.GetValues<AuditEvent>().Length * Enum.GetValues<AuditRole>().Length, Lines(text).Length);
        Assert.All(Lines(text), line => Assert.Matches(AuditLine(), line));
    }

    /// <summary>
    /// A whole working session: sign-up, a refused and an accepted setup key, the first admin, a film,
    /// failed and locked sign-ins, rentals, a refused foreign return, a late return, password changes
    /// and a denied admin operation. The file holds fixed codes and roles only.
    /// </summary>
    [Fact]
    public async Task FullSession_LeavesOnlyCodesAndRolesInTheLog()
    {
        await using var app = await App.StartAsync(_directory);
        var auth = app.Get<IAuthService>();
        var setup = app.Get<ISetupService>();
        var films = app.Get<IFilmService>();
        var rentals = app.Get<IRentalService>();

        await auth.RegisterAsync(CustomerName, CustomerPassword, CustomerEmail, CustomerPhone, Ct);
        await app.AttemptAsync(() => setup.CreateFirstAdminAsync("0000-0000-0000-0000-0000-0000-0000-0000", AdminName, AdminPassword, AdminPassword, Ct));
        var setupKey = await app.AddSetupKeyAsync();
        var admin = await setup.CreateFirstAdminAsync(setupKey, AdminName, AdminPassword, AdminPassword, Ct);
        var filmId = await films.AddAsync(admin, new FilmInput(FilmTitle, "Bilim Kurgu", "2030", Director, "37,25", Poster, About, 1, true), Ct);
        await app.AttemptAsync(() => films.AddAsync(admin, new FilmInput(FilmTitle, "", "1800", Director, "0", "file:///C:/" + FilmTitle, About, 0, true), Ct));

        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await app.AttemptAsync(() => auth.SignInAsync(CustomerName, "yanlis-" + CustomerPassword, Ct));
        }

        await app.AttemptAsync(() => auth.SignInAsync(CustomerName, CustomerPassword, Ct)); // locked
        app.Clock.Advance(LoginPolicy.LockoutDuration);
        var customer = await auth.SignInAsync(CustomerName, CustomerPassword, Ct);

        var film = await films.GetAsync(customer, filmId, Ct);
        var rental = await rentals.RentAsync(customer, rentals.Quote(filmId, film.DailyPrice, 3), Ct);
        await auth.RegisterAsync("baska.musteri", CustomerPassword, "baska@example.com", "5559876543", Ct);
        var other = await auth.SignInAsync("baska.musteri", CustomerPassword, Ct);
        await app.AttemptAsync(() => rentals.RentAsync(other, rentals.Quote(filmId, film.DailyPrice, 2), Ct)); // last copy gone
        await app.AttemptAsync(() => rentals.ReturnAsync(other, rental.RentalId, Ct)); // not theirs
        await app.AttemptAsync(() => films.GetAllAsync(other, Ct)); // admin only
        app.Clock.Advance(TimeSpan.FromDays(5));
        await rentals.ReturnAsync(customer, rental.RentalId, Ct);

        await app.AttemptAsync(() => auth.ChangePasswordAsync(customer, "yanlis-" + CustomerPassword, "Yeni." + CustomerPassword, "Yeni." + CustomerPassword, Ct));
        await auth.ChangePasswordAsync(customer, CustomerPassword, "Yeni." + CustomerPassword, "Yeni." + CustomerPassword, Ct);

        var text = app.CloseAndRead();
        Assert.Equal(2, Regex.Count(text, "Audit auth.account.registration_submitted user"));
        Assert.Contains("Audit setup.key.refused unknown", text, StringComparison.Ordinal);
        Assert.Contains("Audit setup.first_admin.created admin", text, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(text, "Audit film.changed admin"));
        // The refusal of the locked account is counted as one more failure, not as a lock.
        Assert.Equal(LoginPolicy.MaxFailedAttempts + 1, Regex.Count(text, "Audit auth.sign_in.failed unknown"));
        Assert.Equal(2, Regex.Count(text, "Audit auth.sign_in.succeeded user"));
        Assert.Contains("Audit rental.created user", text, StringComparison.Ordinal);
        Assert.Contains("Audit rental.out_of_stock user", text, StringComparison.Ordinal);
        Assert.Contains("Audit access.denied user", text, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(text, "Audit rental.returned user"));
        Assert.Contains("Audit auth.password.change_refused user", text, StringComparison.Ordinal);
        Assert.Contains("Audit auth.password.changed user", text, StringComparison.Ordinal);
        Assert.DoesNotContain("lock", text, StringComparison.OrdinalIgnoreCase);
        Assert.All(Lines(text), line => Assert.Matches(AuditLine(), line));
        AssertNoPersonalData(text);
        AssertAbsent(text, setupKey, setupKey.Replace("-", string.Empty, StringComparison.Ordinal), setupKey[..4]);
    }

    /// <summary>
    /// The log file is readable by the local Windows user, who knows when they made an attempt. An
    /// unknown name, a wrong password, a locked account (with the right password too), an empty field
    /// and a name no account can have leave identical entries, or the file would tell which accounts
    /// exist or are locked.
    /// </summary>
    [Fact]
    public async Task SignInFailures_LeaveTheSameEntriesWhateverTheAccount()
    {
        await using var app = await App.StartAsync(Path.Combine(_directory, "setup"));
        var auth = app.Get<IAuthService>();
        await auth.RegisterAsync(CustomerName, CustomerPassword, CustomerEmail, CustomerPhone, Ct);
        await auth.RegisterAsync("kilitli.hesap", CustomerPassword, "kilitli@example.com", "5550000001", Ct);
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await app.AttemptAsync(() => auth.SignInAsync("kilitli.hesap", "yanlis", Ct));
        }

        app.CloseAndRead();

        (string Name, string? Password)[] attempts =
        [
            ("bilinmeyen.kisi", CustomerPassword),
            (CustomerName, "yanlis-" + CustomerPassword),
            ("kilitli.hesap", "yanlis-" + CustomerPassword),
            ("kilitli.hesap", CustomerPassword),
            (CustomerName, ""),
            (new string('a', 60), CustomerPassword),
        ];
        var outcomes = new List<string[]>();
        foreach (var (name, password) in attempts)
        {
            outcomes.Add(await app.EntriesOfAsync(
                Path.Combine(_directory, outcomes.Count.ToString(CultureInfo.InvariantCulture)),
                () => auth.SignInAsync(name, password, Ct)));
        }

        Assert.Equal(["[INF] Audit auth.sign_in.failed unknown"], outcomes[0]);
        Assert.All(outcomes, o => Assert.Equal(outcomes[0], o));
    }

    /// <summary>
    /// A sign-up with new values, with a taken name, a taken e-mail address and a taken phone number,
    /// each in a log of its own: without the time stamps the entries are identical. Only the new
    /// sign-up adds an account, and the existing account keeps its password.
    /// </summary>
    [Fact]
    public async Task RegistrationOutcomes_LeaveIdenticalEntries()
    {
        await using var app = await App.StartAsync(Path.Combine(_directory, "setup"));
        var auth = app.Get<IAuthService>();
        await auth.RegisterAsync(CustomerName, CustomerPassword, CustomerEmail, CustomerPhone, Ct);
        app.CloseAndRead();

        (string Name, string Email, string Phone)[] attempts =
        [
            ("yeni.musteri", "yeni@kayit.test", "5550000002"),
            (CustomerName, "baska@kayit.test", "5550000003"),
            ("ikinci.musteri", CustomerEmail, "5550000004"),
            ("ucuncu.musteri", "ucuncu@kayit.test", CustomerPhone),
        ];
        var outcomes = new List<string[]>();
        foreach (var (name, email, phone) in attempts)
        {
            outcomes.Add(await app.EntriesOfAsync(
                Path.Combine(_directory, outcomes.Count.ToString(CultureInfo.InvariantCulture)),
                () => auth.RegisterAsync(name, "Baska.Sifre1", email, phone, Ct)));
        }

        Assert.Equal(["[INF] Audit " + AuditCodes.RegistrationSubmitted + " user"], outcomes[0]);
        Assert.All(outcomes, o => Assert.Equal(outcomes[0], o));

        await using var db = await app.Get<IDbContextFactory<FilmDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal([CustomerName, "yeni.musteri"], await db.Users.OrderBy(u => u.Id).Select(u => u.UserName).ToListAsync(Ct));
        await auth.SignInAsync(CustomerName, CustomerPassword, Ct);
    }

    /// <summary>
    /// Returning another account's rental, a rental that does not exist and one already returned leave
    /// identical entries (none), so the file does not tell which rentals exist.
    /// </summary>
    [Fact]
    public async Task ForeignAndMissingReturns_LeaveIdenticalEntries()
    {
        await using var app = await App.StartAsync(Path.Combine(_directory, "setup"));
        var auth = app.Get<IAuthService>();
        var rentals = app.Get<IRentalService>();
        var admin = await app.Get<ISetupService>().CreateFirstAdminAsync(null, AdminName, AdminPassword, AdminPassword, Ct);
        var filmId = await app.Get<IFilmService>().AddAsync(admin, new FilmInput(FilmTitle, "Dram", "2020", Director, "10", null, About, 2, true), Ct);
        await auth.RegisterAsync(CustomerName, CustomerPassword, CustomerEmail, CustomerPhone, Ct);
        await auth.RegisterAsync("baska.musteri", CustomerPassword, "baska@example.com", "5559876543", Ct);
        var owner = await auth.SignInAsync(CustomerName, CustomerPassword, Ct);
        var other = await auth.SignInAsync("baska.musteri", CustomerPassword, Ct);
        var active = await rentals.RentAsync(owner, rentals.Quote(filmId, 10m, 2), Ct);
        var returned = await rentals.RentAsync(other, rentals.Quote(filmId, 10m, 2), Ct);
        await rentals.ReturnAsync(other, returned.RentalId, Ct);
        app.CloseAndRead();

        int[] rentalIds = [active.RentalId, active.RentalId + 1000, returned.RentalId];
        var outcomes = new List<(string Message, string[] Entries)>();
        foreach (var rentalId in rentalIds)
        {
            string message = string.Empty;
            var entries = await app.EntriesOfAsync(
                Path.Combine(_directory, outcomes.Count.ToString(CultureInfo.InvariantCulture)),
                async () =>
                {
                    try
                    {
                        await rentals.ReturnAsync(other, rentalId, Ct);
                    }
                    catch (BusinessRuleException ex)
                    {
                        message = ex.Message;
                        throw;
                    }
                });
            outcomes.Add((message, entries));
        }

        Assert.Empty(outcomes[0].Entries);
        Assert.All(outcomes, o => Assert.Equal(outcomes[0].Entries, o.Entries));
        Assert.All(outcomes, o => Assert.Equal(RentalService.RentalNotFound, o.Message));
        Assert.Single(await rentals.GetMyActiveRentalsAsync(owner, Ct));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AssertNoPersonalData(string text)
    {
        AssertAbsent(text, CustomerName, "deniz", "ornekoglu", CustomerEmail, "example.com", CustomerPhone, "555 123 4567", CustomerPassword);
        AssertAbsent(text, AdminName, "kurucuoglu", AdminPassword, "baska.musteri", "5559876543");
        AssertAbsent(text, FilmTitle, "Kuantum", Director, "Yönetmen", About, Poster, "afis", "37,25", "37.25", "2031", "2030");
        Assert.DoesNotContain("Sifre", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("şifre", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PBKDF2", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAbsent(string text, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.False(text.Contains(value, StringComparison.OrdinalIgnoreCase), "The log contains a value it must not hold.");
        }
    }

    private static string[] Lines(string text) =>
        text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static string WithoutStamp(string line) => StampPattern().Replace(line, string.Empty);

    private const string Stamp = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} ";

    [GeneratedRegex(Stamp)]
    private static partial Regex StampPattern();

    [GeneratedRegex(Stamp + @"\[INF\] Audit [a-z_]+(\.[a-z_]+)+ (user|admin|unknown)$")]
    private static partial Regex AuditLine();

    private string WriteAndRead(Action<Serilog.ILogger> write)
    {
        using (var logger = AppLogging.CreateLogger(_directory))
        {
            write(logger);
        }

        return File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
    }

    private string DatedFile(int age, int sequence = 0)
    {
        var day = DateTime.Today.AddDays(-age).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var suffix = sequence == 0 ? string.Empty : "_" + sequence.ToString("000", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, $"{AppLogging.FilePrefix}{day}{suffix}.log");
    }

    private static int AgeOf(string file)
    {
        var day = DateTime.ParseExact(Path.GetFileName(file).Substring(AppLogging.FilePrefix.Length, 8), "yyyyMMdd", CultureInfo.InvariantCulture);
        return (DateTime.Today - day).Days;
    }

    private static int SequenceOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var underscore = name.LastIndexOf('_');
        return underscore < 0 ? 0 : int.Parse(name[(underscore + 1)..], CultureInfo.InvariantCulture);
    }

    private static Exception Throw(Exception exception)
    {
        try
        {
            throw exception;
        }
#pragma warning disable CA1031 // Test helper captures the stack trace.
        catch (Exception caught)
#pragma warning restore CA1031
        {
            return caught;
        }
    }

    /// <summary>
    /// The application's services on an empty SQLite in-memory database (no demonstration data), with
    /// the audit events going to the current log file. Every refusal is recorded with
    /// <see cref="ErrorReporter.Record"/>, as the forms do.
    /// </summary>
    private sealed class App : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly SwitchableAudit _audit = new();
        private ServiceProvider? _provider;
        private Serilog.Core.Logger? _logger;
        private string _directory = string.Empty;

        public FakeTimeProvider Clock { get; } = new(Now);

        public static async Task<App> StartAsync(string directory)
        {
            var app = new App();
            await app._connection.OpenAsync(Ct);
            var services = new ServiceCollection();
            services.AddDataAccess(b => b.UseSqlite(app._connection), new DataAccessOptions { SeedDemoData = false });
            services.AddSingleton<TimeProvider>(app.Clock);
            services.AddSingleton<IPasswordHasher>(new PasswordHasher(iterations: 1_000));
            services.AddSingleton<IAuditLog>(app._audit);
            services.AddSingleton(new SetupKeyEnvironment(null));
            services.AddSingleton(new SetupKeyThrottle(TimeSpan.Zero));
            services.AddBusinessServices(new RentalPolicy { DailyLateFee = 10m, MaxRentalDays = 30 });
            app._provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
            await app.Get<IDatabaseInitializer>().InitializeAsync(Ct);
            app.Open(directory);
            return app;
        }

        public T Get<T>()
            where T : notnull => _provider!.GetRequiredService<T>();

        public async Task AttemptAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
#pragma warning disable CA1031 // Every failure goes to the log, as the forms do.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorReporter.Record(_logger!, ex);
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> as a form would, with its audit events and records in a log of
        /// their own, and returns the entries of that log without their time stamps (none when nothing
        /// was written).
        /// </summary>
        public async Task<string[]> EntriesOfAsync(string directory, Func<Task> action)
        {
            Open(directory);
            await AttemptAsync(action);
            Close();
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory).SelectMany(f => Lines(File.ReadAllText(f))).Select(WithoutStamp).ToArray()
                : [];
        }

        public string CloseAndRead()
        {
            Close();
            return File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        }

        /// <summary>Stores a setup key as the database owner's script does and returns it in groups of four.</summary>
        public async Task<string> AddSetupKeyAsync()
        {
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            await using var db = await Get<IDbContextFactory<FilmDbContext>>().CreateDbContextAsync(Ct);
            db.SetupKeys.Add(new SetupKey
            {
                KeyHash = SHA256.HashData(Encoding.ASCII.GetBytes(key)),
                CreatedAt = Clock.GetUtcNow().UtcDateTime,
                ExpiresAt = Clock.GetUtcNow().UtcDateTime + SetupKeyPolicy.Lifetime,
            });
            await db.SaveChangesAsync(Ct);
            return SetupKeyPolicy.Format(key);
        }

        public async ValueTask DisposeAsync()
        {
            _logger?.Dispose();
            if (_provider is not null)
            {
                await _provider.DisposeAsync();
            }

            await _connection.DisposeAsync();
        }

        private void Open(string directory)
        {
            _directory = directory;
            _logger = AppLogging.CreateLogger(directory);
            _audit.Target = new SerilogAuditLog(_logger);
        }

        private void Close()
        {
            _audit.Target = null;
            _logger?.Dispose();
            _logger = null;
        }
    }

    /// <summary>Forwards audit events to the log that is open at the moment.</summary>
    private sealed class SwitchableAudit : IAuditLog
    {
        public SerilogAuditLog? Target { get; set; }

        public void Record(AuditEvent auditEvent, AuditRole role) => Target?.Record(auditEvent, role);
    }
}
