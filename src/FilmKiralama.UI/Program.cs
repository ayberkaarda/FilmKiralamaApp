using System.Globalization;
using FilmKiralama.Business;
using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Data;
using FilmKiralama.Data.Persistence;
using FilmKiralama.UI.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FilmKiralama.UI
{
    internal static class Program
    {
        internal const string ConnectionStringName = "FilmKiralama";

        /// <summary>
        /// Application entry point and composition root. The application never creates setup keys; only
        /// the database owner does, with docs/issue-setup-key.sql.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Log.Logger = AppLogging.CreateLogger(AppLogging.DefaultDirectory);
            try
            {
                Log.Information("Application started");
                Run();
            }
            finally
            {
                Log.Information("Application stopped");
                Log.CloseAndFlush();
            }
        }

        private static void Run()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ErrorReporter.Show(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.IsTerminating)
                {
                    Log.Fatal("Unhandled exception terminates the application");
                }

                ErrorReporter.Show(e.ExceptionObject as Exception);
                if (e.IsTerminating)
                {
                    Log.CloseAndFlush();
                }
            };

            ApplicationConfiguration.Initialize();

            var configuration = BuildConfiguration();
            var (policy, policyProblems) = AppConfiguration.ReadRentalPolicy(configuration);
            if (policyProblems.Count > 0)
            {
                Log.Warning("The rental settings are invalid");

                // The problems name settings and limits only.
                Dialogs.Show(
                    null,
                    "appsettings.json içindeki \"Rental\" ayarları geçersiz:\n\n" + string.Join("\n", policyProblems),
                    AppText.Title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var connectionString = AppConfiguration.ResolveConnectionString(configuration);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Warning("No database connection string is configured");
                Dialogs.Show(
                    null,
                    "Veritabanı bağlantı ayarı bulunamadı.\n\n"
                    + "appsettings.example.json dosyasını appsettings.json olarak kopyalayıp bağlantı bilgisini girin, "
                    + "kullanıcı gizli ayarlarına (user secrets) ekleyin ya da FILM_CONNECTION ortam değişkenini tanımlayın.",
                    AppText.Title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var options = new DataAccessOptions { SeedDemoData = AppConfiguration.IsDemoRequested(configuration) };
            if (options.SeedDemoData)
            {
                Log.Warning("Demo mode is on: demo accounts with published passwords are added to an empty database");
            }

            using var serviceProvider = ConfigureServices(connectionString, options, policy);
            bool setupRequired;
            bool adminMissing;
            try
            {
                // No synchronization context exists yet, so blocking here cannot deadlock.
                serviceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
                var setupService = serviceProvider.GetRequiredService<ISetupService>();
                setupRequired = setupService.IsSetupRequiredAsync().GetAwaiter().GetResult();
                adminMissing = setupRequired || setupService.IsAdminMissingAsync().GetAwaiter().GetResult();
            }
#pragma warning disable CA1031 // Startup boundary: report and exit instead of crashing.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorReporter.Show(ex);
                return;
            }

            var formFactory = serviceProvider.GetRequiredService<IFormFactory>();

            // On an empty database the first admin is created with a password of the installer's own
            // choosing; there is no default account. Closing the setup ends the application. A database
            // with accounts but no admin keeps its sign-in, which offers the setup with a setup key.
            if (setupRequired)
            {
                using var setup = formFactory.Create<IlkKurulumForm>();
                if (setup.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            var signIn = formFactory.Create<LoginForm>();
            signIn.OfferAdminSetup = adminMissing && !setupRequired;
            Application.Run(signIn);
        }

        private static IConfiguration BuildConfiguration() =>
            new ConfigurationBuilder()
                .AddSettingsFile(AppContext.BaseDirectory, AppConfiguration.SettingsFileName)
                .AddUserSecrets(typeof(Program).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();

        private static ServiceProvider ConfigureServices(string connectionString, DataAccessOptions options, RentalPolicy policy)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuditLog>(new SerilogAuditLog(Log.Logger));
            services.AddSqlServerDataAccess(connectionString, options);
            services.AddBusinessServices(policy);
            services.AddSingleton<IFormFactory, FormFactory>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        }
    }

    /// <summary>How the application reads its settings.</summary>
    internal static class AppConfiguration
    {
        public const string SettingsFileName = "appsettings.json";

        /// <summary>Settings key of the demonstration data switch.</summary>
        public const string DemoSettingKey = "Seed:Demo";

        public const string DailyLateFeeKey = RentalPolicy.SectionName + ":DailyLateFee";

        public const string MaxRentalDaysKey = RentalPolicy.SectionName + ":MaxRentalDays";

        /// <summary>Adds an optional JSON settings file located in <paramref name="basePath"/>.</summary>
        public static IConfigurationBuilder AddSettingsFile(this IConfigurationBuilder builder, string basePath, string fileName) =>
            builder
                .SetBasePath(basePath)
                .AddJsonFile(fileName, optional: true, reloadOnChange: false);

        /// <summary>
        /// Resolution order (last wins): appsettings.json, user secrets, ConnectionStrings__FilmKiralama
        /// environment variable, FILM_CONNECTION environment variable.
        /// </summary>
        public static string? ResolveConnectionString(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var fromEnvironment = configuration[DataServiceCollectionExtensions.ConnectionEnvironmentVariable];
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? configuration.GetConnectionString(Program.ConnectionStringName)
                : fromEnvironment;
        }

        /// <summary>
        /// Whether the demonstration data was asked for, through FILM_DEMO ("1" or "true") or the Seed:Demo
        /// setting. Anything else, including a missing or malformed value, means no.
        /// </summary>
        public static bool IsDemoRequested(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return IsYes(configuration[DataServiceCollectionExtensions.DemoEnvironmentVariable])
                || IsYes(configuration[DemoSettingKey]);
        }

        /// <summary>
        /// Reads the <c>Rental</c> section; missing values keep their defaults. A value that is not a number
        /// (JSON numbers are read with the invariant culture) or is out of range is reported as a problem.
        /// </summary>
        public static (RentalPolicy Policy, IReadOnlyList<string> Problems) ReadRentalPolicy(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var defaults = new RentalPolicy();
            var problems = new List<string>();

            var dailyLateFee = defaults.DailyLateFee;
            if (configuration[DailyLateFeeKey] is { } feeText
                && !decimal.TryParse(feeText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out dailyLateFee))
            {
                problems.Add(DailyLateFeeKey + " bir tutar olmalıdır (örnek: 10.50).");
                dailyLateFee = defaults.DailyLateFee;
            }

            var maxRentalDays = defaults.MaxRentalDays;
            if (configuration[MaxRentalDaysKey] is { } daysText
                && !int.TryParse(daysText, NumberStyles.None, CultureInfo.InvariantCulture, out maxRentalDays))
            {
                problems.Add(MaxRentalDaysKey + " bir gün sayısı olmalıdır.");
                maxRentalDays = defaults.MaxRentalDays;
            }

            var policy = new RentalPolicy { DailyLateFee = dailyLateFee, MaxRentalDays = maxRentalDays };
            problems.AddRange(policy.Validate());
            return (problems.Count == 0 ? policy : defaults, problems);
        }

        private static bool IsYes(string? value) =>
            value is not null
            && (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }
}
