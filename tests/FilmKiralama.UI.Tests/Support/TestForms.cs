using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests.Support;

/// <summary>
/// Creates every screen with test doubles for its dependencies and loaded with long titles and names, so no
/// window, database or message window is involved and the layout is checked with the hardest content.
/// </summary>
internal static class TestForms
{
    /// <summary>A film title at the longest the catalog accepts, so it needs an ellipsis on a card.</summary>
    public const string LongTitle = "Kuyruklu Yıldız Pansiyonunda Unutulan Son Mektuplar";

    /// <summary>A user name at the longest a stored account can have.</summary>
    public const string LongUserName = "zeynepnurkaramanliogluBuyukcekmeceliTasdelenogulla";

    /// <summary>The screens of the application by class name.</summary>
    public static readonly string[] Names =
    [
        nameof(LoginForm), nameof(RegisterForm), nameof(IlkKurulumForm), nameof(SifreDegistirmeForm), nameof(AnaForm),
        nameof(KiralamaForm), nameof(KullaniciProfil), nameof(DegerlendirmeForm), nameof(IzlemeListesiForm), nameof(OnerilerForm),
        nameof(AdminPanel), nameof(KiralananFilmlerPanel), nameof(KullaniciYonetimPanel), nameof(YorumYonetimForm), nameof(RaporlarForm),
    ];

    public static AppDialog Create(string name) => name switch
    {
        nameof(LoginForm) => new LoginForm(new PendingAuthService(), new NoFormsFactory()) { OfferAdminSetup = true },
        nameof(RegisterForm) => new RegisterForm(new PendingAuthService()),
        nameof(IlkKurulumForm) => new IlkKurulumForm(new PendingSetupService()) { RequireSetupKey = true },
        nameof(SifreDegistirmeForm) => new SifreDegistirmeForm(new PendingAuthService(), Sessions.User),
        nameof(AnaForm) => Catalog(),
        nameof(KiralamaForm) => Rental(),
        nameof(KullaniciProfil) => Profile(),
        nameof(DegerlendirmeForm) => Review(),
        nameof(IzlemeListesiForm) => Watchlist(),
        nameof(OnerilerForm) => Recommendations(),
        nameof(AdminPanel) => AdminPanel(),
        nameof(KiralananFilmlerPanel) => Rentals(),
        nameof(KullaniciYonetimPanel) => Accounts(),
        nameof(YorumYonetimForm) => Moderation(),
        nameof(RaporlarForm) => Reports(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>Films with a long title, one out of stock and one with its last copy on the shelf.</summary>
    public static ControlledFilmService Films()
    {
        var films = new ControlledFilmService();
        films.Films[0].Title = LongTitle;
        films.Films[1].AvailableCopies = 0;
        var last = ControlledFilmService.Film(3, "Bakır Kanatlar", "Macera");
        last.AvailableCopies = 1;
        films.Films.Add(last);
        return films;
    }

    public static AnaForm Catalog()
    {
        var films = Films();
        var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        _ = form.RefreshAsync();
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films, total: 30));
        return form;
    }

    public static KiralamaForm Rental()
    {
        var films = Films();
        var reviews = new ControlledReviewService
        {
            FilmReviews = new FilmReviews(4.5m, 2, [new PublicReview("O***", 4, string.Join(" ", Enumerable.Repeat("Uzun bir yorum satırı.", 20)), new DateOnly(2026, 3, 1))]),
        };
        var form = new KiralamaForm(films, new ControlledRentalService(), reviews, Sessions.User, 1) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadFilmAsync().IsCompletedSuccessfully);
        return form;
    }

    public static KullaniciProfil Profile()
    {
        var rentals = new ControlledRentalService();
        rentals.Rentals[0] = rentals.Rentals[0] with { FilmTitle = LongTitle };
        var reviews = new ControlledReviewService();
        reviews.Reviewable.Add(new ReviewableFilm(3, LongTitle, "Bilim Kurgu", 2024, null, null));
        var form = new KullaniciProfil(rentals, reviews, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        Assert.True(form.LoadReviewableAsync().IsCompletedSuccessfully);
        return form;
    }

    /// <summary>The customer's own review, hidden by an admin, so the notice is shown too.</summary>
    public static DegerlendirmeForm Review()
    {
        var reviews = new ControlledReviewService { Own = new OwnReview(7, 2, 2, "Eski yorum", IsHidden: true) };
        var form = new DegerlendirmeForm(reviews, Sessions.User, new ReviewableFilm(2, LongTitle, "Dram", 2015, 7, 2)) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }

    public static IzlemeListesiForm Watchlist()
    {
        var watchlist = new ControlledWatchlistService();
        watchlist.Entries.Add(new WatchlistView(8, new CatalogItem(ControlledFilmService.Film(3, LongTitle, "Bilim Kurgu"), 3.9m, 12), new DateOnly(2026, 3, 2)));
        var form = new IzlemeListesiForm(watchlist, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }

    public static OnerilerForm Recommendations()
    {
        var recommendations = new ControlledRecommendationService();
        recommendations.Films.Add(new CatalogItem(ControlledFilmService.Film(3, LongTitle, "Gerilim"), null, 0));
        var form = new OnerilerForm(recommendations, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }

    /// <summary>The admin panel with a film chosen, so the editor is filled.</summary>
    public static AdminPanel AdminPanel()
    {
        var films = Films();
        var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts(), Posters = NoPosters() };
        Assert.True(form.RefreshAsync().IsCompletedSuccessfully);
        form.SelectFilm(films.Films[0]);
        return form;
    }

    public static KiralananFilmlerPanel Rentals()
    {
        var rentals = new ControlledRentalService();
        rentals.Rentals[0] = rentals.Rentals[0] with { FilmTitle = LongTitle, UserName = LongUserName };
        var form = new KiralananFilmlerPanel(rentals, Films(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.RefreshAsync().IsCompletedSuccessfully);
        return form;
    }

    public static KullaniciYonetimPanel Accounts()
    {
        var accounts = new ControlledAccountService();
        accounts.Accounts.Add(new UserSummary(3, LongUserName, LongUserName + "@example.com", "555 123 4567", UserRole.User, 0));
        var form = new KullaniciYonetimPanel(accounts, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadUsersAsync().IsCompletedSuccessfully);
        form.SelectUser(accounts.Accounts[2]);
        return form;
    }

    public static YorumYonetimForm Moderation()
    {
        var reviews = new ControlledReviewService();
        var form = new YorumYonetimForm(reviews, Sessions.Admin) { Prompts = new RecordingPrompts() };
        _ = form.LoadAsync(1);
        reviews.ListRequests[0].Answer.SetResult(new PagedResult<ReviewDetails>(
            [ControlledReviewService.Review(1) with { FilmTitle = LongTitle, UserName = LongUserName, Comment = string.Join(" ", Enumerable.Repeat("Çok uzun bir yorum.", 25)) }, ControlledReviewService.Review(2, hidden: true)],
            51,
            1,
            50));
        return form;
    }

    public static RaporlarForm Reports()
    {
        var reports = new ControlledReportService();
        var form = new RaporlarForm(reports, Sessions.Admin, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 3, 31, 10, 0, 0, TimeSpan.Zero)))
        {
            Prompts = new RecordingPrompts(),
            SavePrompt = new FixedSavePrompt(null),
        };
        _ = form.LoadAsync();
        reports.MostRentedRequests[0].Answer.SetResult([new(1, LongTitle, "Gerilim", 4), new(2, "Son Vapur", "Dram", 1)]);
        return form;
    }

    /// <summary>A poster loader that never downloads: every poster stays the placeholder.</summary>
    public static PosterLoader NoPosters() => new((_, _) => Task.FromResult<Image?>(null));

    /// <summary>Finds a control of a screen by name; for the tests that build screens through this class.</summary>
    public static T Get<T>(Control root, string name)
        where T : Control => Find<T>(root, name);
}
