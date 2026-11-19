using System.Drawing;
using System.Globalization;
using System.Reflection;
using FilmKiralama.Business;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.Screenshots;

/// <summary>
/// An invented rental shop in memory: made-up films with poster art drawn in code, directors, customers and
/// reviews, e-mail addresses on the reserved example.com domain, so the screenshots never show a real film,
/// poster, studio or person and no database is needed. Every call completes synchronously.
/// </summary>
internal sealed class SyntheticCinema :
    IAuthService, ISetupService, IFilmService, IRentalService, IReviewService, IWatchlistService, IReportService, IRecommendationService, IAccountService
{
    /// <summary>The day the shop stands on, so dates in the screenshots do not depend on when the tool runs.</summary>
    public static readonly DateOnly Today = new(2027, 3, 18);

    private readonly List<CatalogItem> _films =
    [
        Item(1, "Kehribar Rüzgârı", "Dram", 2021, "Ilgın Çamlıbel", 42.5m, 3, 2, 4.4m, 18, "Bir rüzgâr gülü ustasının kıyı kasabasında geçen son yazı. Kasabanın unutulmuş hikâyeleri, her gün değişen rüzgârla birlikte geri döner."),
        Item(2, "Gece Yarısı Tramvayı", "Gerilim", 2023, "Tolga Sarıkaya", 55m, 2, 1, 4.1m, 26, "Son seferini yapan tramvayda kaybolan bir çanta, yedi yolcunun gecesini birbirine bağlar."),
        Item(3, "Bakır Kanatlar", "Macera", 2019, "Ece Taşkıran", 38m, 4, 3, 3.8m, 11, null),
        Item(4, "Mavi Tuz Fabrikası", "Komedi", 2022, "Kaan Sözer", 35m, 2, 0, 4.6m, 31, "Batmak üzere olan bir tuz fabrikasının işçileri, kasabanın en tuhaf festivalini düzenlemeye karar verir."),
        Item(5, "Dokuzuncu Fener", "Bilim Kurgu", 2024, "Selin Yağmurlu", 60m, 3, 3, 4.2m, 9, null),
        Item(6, "Kum Saati Kervanı", "Macera", 2018, "Bora Kandemir", 30m, 2, 1, 3.5m, 7, null),
        Item(7, "Pusula Gölü", "Belgesel", 2020, "Ilgın Çamlıbel", 28m, 1, 1, 4.8m, 5, null),
        Item(8, "Sessiz Kartografya", "Dram", 2025, "Nehir Aksoylu", 58m, 2, 2, null, 0, null),
        Item(9, "Kırık Neon Bulvarı", "Gerilim", 2017, "Tolga Sarıkaya", 33m, 3, 2, 3.9m, 14, null),
        Item(10, "Turuncu Balon Operası", "Animasyon", 2022, "Defne Ulusoy", 40m, 5, 4, 4.5m, 22, null),
        Item(11, "Yedi Dağın Postacısı", "Dram", 2016, "Ece Taşkıran", 26m, 2, 2, 4.0m, 12, null),
        Item(12, "Kuyruklu Yıldız Pansiyonu", "Komedi", 2023, "Kaan Sözer", 45m, 3, 2, 3.7m, 8, null),
        Item(13, "Gümüş Çay Bahçesi", "Dram", 2019, "Nehir Aksoylu", 32m, 2, 0, 4.3m, 15, null),
        Item(14, "Mart Ayının Mektupları", "Dram", 2021, "Defne Ulusoy", 36m, 1, 1, 4.1m, 6, null),
        Item(15, "Kristal Kumul", "Bilim Kurgu", 2026, "Bora Kandemir", 65m, 2, 2, null, 0, null),
    ];

    private readonly List<UserSummary> _accounts =
    [
        new(1, "yonetici", "yonetici@example.com", "555 010 2030", UserRole.Admin),
        new(2, "ilgin", "ilgin@example.com", "555 210 4455", UserRole.User),
        new(3, "mert.akdogan", "mert.akdogan@example.com", null, UserRole.User),
        new(4, "deniz.su", "deniz.su@example.com", "555 360 1122", UserRole.User),
        new(5, "kasaci", "kasa@example.com", "555 470 9988", UserRole.Admin),
        new(6, "zeynepnurkaramanlioglu", "zeynep.nur@example.com", null, UserRole.User),
    ];

    public SyntheticCinema()
    {
        Admin = Session(1, "yonetici", UserRole.Admin);
        User = Session(2, "ilgin", UserRole.User);
    }

    public UserSession Admin { get; }

    public UserSession User { get; }

    public RentalPolicy Policy { get; } = new() { DailyLateFee = 10m, MaxRentalDays = 30 };

    public IReadOnlyList<CatalogItem> Films => _films;

    /// <summary>A poster loader that draws the invented films' posters instead of downloading anything.</summary>
    public PosterLoader Posters => new((uri, _) => Task.FromResult<Image?>(PosterFor(uri)));

    /// <summary>The address an invented film's poster is stored under; a reserved example domain.</summary>
    public static string PosterUrl(int filmId) => string.Create(CultureInfo.InvariantCulture, $"https://afis.example.com/film-{filmId}.png");

    private Bitmap? PosterFor(Uri uri)
    {
        var name = uri.Segments[^1];
        if (!name.StartsWith("film-", StringComparison.Ordinal) || !int.TryParse(name[5..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var film = _films.FirstOrDefault(f => f.Film.Id == id)?.Film;
        return film is null ? null : PosterArt.Draw(id, film.Title, film.Year);
    }

    private static CatalogItem Item(int id, string title, string genre, int year, string director, decimal price, int copies, int available, decimal? average, int count, string? about) =>
        new(
            new Film
            {
                Id = id,
                Title = title,
                Genre = genre,
                Year = year,
                Director = director,
                DailyPrice = price,
                TotalCopies = copies,
                AvailableCopies = available,
                ImageUrl = PosterUrl(id),
                About = about ?? "Kısa bir açıklama: kendi hikâyesini arayan karakterler, beklenmedik bir yolculuk ve unutulmaz bir final.",
                IsListed = true,
            },
            average,
            count);

    /// <summary>A session as the services would issue it; its constructor is internal to the business layer.</summary>
    private static UserSession Session(int id, string name, UserRole role) =>
        (UserSession)Activator.CreateInstance(
            typeof(UserSession),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [id, name, role, 0],
            culture: null)!;

    private static RentalDetails Rental(int id, CatalogItem item, int userId, string userName, DateOnly rentedOn, int days) =>
        new(id, item.Film.Id, item.Film.Title, item.Film.Genre, item.Film.Year, item.Film.Director, item.Film.ImageUrl, userId, userName, rentedOn, rentedOn.AddDays(days), item.Film.DailyPrice * days, RentalStatus.Active);

    // Authentication and setup.
    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default) => Task.FromResult(User);

    public Task RegisterAsync(string? userName, string? password, string? email, string? phone, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ChangePasswordAsync(UserSession session, string? currentPassword, string? newPassword, string? newPasswordRepeat, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public SetupKeyPrefill GetEnvironmentKey() => new(null, false);

    public Task<UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) =>
        Task.FromResult(Admin);

    // Films.
    public Task<PagedResult<CatalogItem>> SearchCatalogAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Search(query, listedOnly: true));

    public Task<PagedResult<CatalogItem>> SearchAllAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Search(query, listedOnly: false));

    private PagedResult<CatalogItem> Search(CatalogQuery query, bool listedOnly)
    {
        var found = _films
            .Where(i => !listedOnly || i.Film.IsListed)
            .Where(i => !query.AvailableOnly || i.IsAvailable)
            .Where(i => query.Genre is null || i.Film.Genre == query.Genre)
            .Where(i => query.Title is null || i.Film.Title.Contains(query.Title, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(i => i.Film.Title, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), false))
            .ToList();
        var page = Math.Max(1, query.Page);
        return new PagedResult<CatalogItem>([.. found.Skip((page - 1) * query.PageSize).Take(query.PageSize)], found.Count, page, query.PageSize);
    }

    public Task<IReadOnlyList<Film>> GetCatalogAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Film>>([.. _films.Select(i => i.Film)]);

    public Task<FilmFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FilmFilterOptions(
            [.. _films.Select(i => i.Film.Genre).Distinct().Order()],
            [.. _films.Select(i => i.Film.Director).Distinct().Order()],
            [.. _films.Select(i => i.Film.Year).Distinct().OrderDescending()]));

    public Task<Film> GetAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_films.Single(i => i.Film.Id == filmId).Film);

    public Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Film>>([.. _films.Select(i => i.Film)]);

    public Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default) => Task.FromResult(16);

    public Task UpdateAsync(UserSession session, int filmId, FilmInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Rentals.
    public Task<RentalQuote> QuoteAsync(int filmId, decimal dailyPrice, int days, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RentalQuote(filmId, days, dailyPrice, dailyPrice * days, Today, Today.AddDays(days)));

    public Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RentalReceipt(40, quote.Total, quote.StartsOn, quote.DueOn));

    public Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>(
        [
            Rental(31, _films[1], 2, "ilgin", Today.AddDays(-2), 3),
            Rental(32, _films[9], 2, "ilgin", Today.AddDays(-1), 7),
            Rental(33, _films[6], 2, "ilgin", Today.AddDays(-6), 4),
        ]);

    public Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>(
        [
            Rental(31, _films[1], 2, "ilgin", Today.AddDays(-2), 3),
            Rental(32, _films[9], 2, "ilgin", Today.AddDays(-1), 7),
            Rental(33, _films[6], 2, "ilgin", Today.AddDays(-6), 4),
            Rental(34, _films[3], 3, "mert.akdogan", Today.AddDays(-9), 5),
            Rental(35, _films[12], 4, "deniz.su", Today.AddDays(-3), 10),
            Rental(36, _films[0], 6, "zeynepnurkaramanlioglu", Today, 2),
        ]);

    public Task<ReturnPreview> PreviewReturnAsync(RentalDetails rental, CancellationToken cancellationToken = default) => Task.FromResult(new ReturnPreview(0, 0m));

    public Task<ReturnReceipt> ReturnAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default) => Task.FromResult(new ReturnReceipt(0, 0m));

    // Reviews.
    public Task SaveAsync(UserSession session, int filmId, ReviewInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<OwnReview?> GetOwnAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OwnReview?>(filmId == 4 ? new OwnReview(51, 4, 5, "Fabrikadaki festival sahnesinde gülmekten koltuktan düşecektim. Müzikleri de çok iyi.", false) : null);

    public Task DeleteOwnAsync(UserSession session, int reviewId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FilmReviews> GetFilmReviewsAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FilmReviews(
            4.1m,
            26,
            [
                new("D***", 5, "Tramvay sahnesindeki gerilim son dakikaya kadar hiç düşmüyor. Işık ve ses kullanımı çok başarılı.", Today.AddDays(-2)),
                new("M***", 4, "Hikâye biraz yavaş başlıyor ama ikinci yarı bunu fazlasıyla telafi ediyor.", Today.AddDays(-5)),
                new("Z***", 4, null, Today.AddDays(-9)),
                new("K***", 3, "Oyunculuklar iyi, final tahmin edilebilirdi.", Today.AddDays(-14)),
            ]));

    public Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReviewableFilm>>(
        [
            new(4, "Mavi Tuz Fabrikası", "Komedi", 2022, 51, 5),
            new(11, "Yedi Dağın Postacısı", "Dram", 2016, null, null),
            new(13, "Gümüş Çay Bahçesi", "Dram", 2019, 52, 4),
            new(3, "Bakır Kanatlar", "Macera", 2019, null, null),
        ]);

    public Task<PagedResult<ReviewDetails>> GetAllAsync(UserSession session, bool hiddenOnly, int page, CancellationToken cancellationToken = default)
    {
        ReviewDetails[] all =
        [
            new(61, 2, "Gece Yarısı Tramvayı", 4, "deniz.su", 5, "Tramvay sahnesindeki gerilim son dakikaya kadar hiç düşmüyor.", false, new DateTime(2027, 3, 16, 20, 15, 0, DateTimeKind.Utc)),
            new(60, 4, "Mavi Tuz Fabrikası", 2, "ilgin", 5, "Fabrikadaki festival sahnesinde gülmekten koltuktan düşecektim.", false, new DateTime(2027, 3, 15, 18, 40, 0, DateTimeKind.Utc)),
            new(59, 9, "Kırık Neon Bulvarı", 3, "mert.akdogan", 1, "Bu filmi izleyen herkese ücretsiz tekrar kiralama kodu: SAHTE-123", true, new DateTime(2027, 3, 14, 9, 5, 0, DateTimeKind.Utc)),
            new(58, 10, "Turuncu Balon Operası", 6, "zeynepnurkaramanlioglu", 4, "Çocuklarla izledik, şarkılar hâlâ dilimizde.", false, new DateTime(2027, 3, 12, 21, 0, 0, DateTimeKind.Utc)),
            new(57, 13, "Gümüş Çay Bahçesi", 2, "ilgin", 4, null, false, new DateTime(2027, 3, 10, 16, 30, 0, DateTimeKind.Utc)),
            new(56, 1, "Kehribar Rüzgârı", 4, "deniz.su", 5, "Görüntü yönetimi nefes kesici; her kare bir kartpostal gibi.", false, new DateTime(2027, 3, 8, 22, 10, 0, DateTimeKind.Utc)),
            new(55, 6, "Kum Saati Kervanı", 3, "mert.akdogan", 3, "Macera güzel ama süre biraz uzun.", false, new DateTime(2027, 3, 6, 19, 45, 0, DateTimeKind.Utc)),
        ];
        var shown = hiddenOnly ? [.. all.Where(r => r.IsHidden)] : all;
        return Task.FromResult(new PagedResult<ReviewDetails>(shown, hiddenOnly ? shown.Length : 57, page, 50));
    }

    public Task SetHiddenAsync(UserSession session, int reviewId, bool hidden, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Watchlist.
    Task<bool> IWatchlistService.AddAsync(UserSession session, int filmId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RemoveAsync(UserSession session, int entryId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<WatchlistView>> GetMineAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        var unlisted = new CatalogItem(new Film { Id = 99, Title = "Eski Sinema Kulübü", IsListed = false }, null, 0);
        return Task.FromResult<IReadOnlyList<WatchlistView>>(
        [
            new(71, _films[4], Today.AddDays(-1)),
            new(72, _films[3], Today.AddDays(-3)),
            new(73, _films[13], Today.AddDays(-6)),
            new(74, _films[12], Today.AddDays(-12)),
            new(75, unlisted, Today.AddDays(-40)),
        ]);
    }

    // Reports.
    public Task<IReadOnlyList<RentalCountRow>> GetMostRentedAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalCountRow>>(
        [
            new(4, "Mavi Tuz Fabrikası", "Komedi", 31),
            new(2, "Gece Yarısı Tramvayı", "Gerilim", 26),
            new(10, "Turuncu Balon Operası", "Animasyon", 22),
            new(1, "Kehribar Rüzgârı", "Dram", 18),
            new(13, "Gümüş Çay Bahçesi", "Dram", 15),
            new(9, "Kırık Neon Bulvarı", "Gerilim", 14),
            new(11, "Yedi Dağın Postacısı", "Dram", 12),
            new(3, "Bakır Kanatlar", "Macera", 11),
        ]);

    public Task<IReadOnlyList<OverdueReportRow>> GetOverdueAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OverdueReportRow>>(
        [
            new(new OverdueRentalRow(34, "Mavi Tuz Fabrikası", "mert.akdogan", Today.AddDays(-9), Today.AddDays(-4)), 4, 40m),
            new(new OverdueRentalRow(29, "Kum Saati Kervanı", "deniz.su", Today.AddDays(-12), Today.AddDays(-2)), 2, 20m),
        ]);

    public Task<LateFeeReport> GetLateFeesAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LateFeeReport(
            [
                new(21, "Bakır Kanatlar", "deniz.su", Today.AddDays(-20), Today.AddDays(-17), 30m),
                new(18, "Kırık Neon Bulvarı", "mert.akdogan", Today.AddDays(-25), Today.AddDays(-24), 10m),
            ],
            40m));

    public Task<ExportFile> ExportCsvAsync(UserSession session, ReportKind kind, ReportRange range, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ExportFile("rapor.csv", []));

    public void RecordExport(UserSession session)
    {
    }

    // Recommendations.
    public Task<IReadOnlyList<CatalogItem>> GetForMeAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogItem>>([_films[0], _films[12], _films[13], _films[7]]);

    // Accounts.
    Task<IReadOnlyList<UserSummary>> IAccountService.GetAllAsync(UserSession session, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(_accounts);

    public Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default) => Task.FromResult(7);

    public Task UpdateAsync(UserSession session, int userId, AccountInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task IAccountService.DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
