using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>The application ships only its own artwork, and its windows carry neutral titles.</summary>
public sealed class BrandingTests
{
    [Fact]
    public void EmbeddedArtwork_IsTheApplicationsOwn()
    {
        var resources = typeof(AppAssets).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["FilmKiralama.UI.Assets.app.ico", "FilmKiralama.UI.Assets.background.png", "FilmKiralama.UI.Assets.hero.png", "FilmKiralama.UI.Assets.logo-256.png", "FilmKiralama.UI.Assets.poster.png"],
            resources.Where(r => r.Contains(".Assets.", StringComparison.Ordinal)));
        Assert.DoesNotContain(resources, r => r.EndsWith(".resources", StringComparison.Ordinal) && !r.Contains("ReaLTaiizor", StringComparison.Ordinal));
        Assert.Equal(new Size(545, 737), AppAssets.Hero.Size);

        // The placeholder has the 2:3 shape of a poster, at the largest size a downloaded poster is kept at.
        Assert.Equal(new Size(PosterLoader.MaxShownWidth, PosterLoader.MaxShownHeight), AppAssets.Poster.Size);
        Assert.Equal(new Size(256, 256), AppAssets.Logo.Size);
        Assert.NotNull(AppAssets.Icon);
    }

    [Fact]
    public void Forms_HaveNeutralTurkishTitles() => Run(() =>
    {
        var films = new ControlledFilmService();
        var rentals = new ControlledRentalService();
        var forms = new NoFormsFactory();
        var reviews = new ControlledReviewService();
        var watchlist = new ControlledWatchlistService();
        Form[] all =
        [
            new LoginForm(new PendingAuthService(), forms),
            new RegisterForm(new PendingAuthService()),
            new SifreDegistirmeForm(new PendingAuthService(), Sessions.User),
            new AnaForm(films, watchlist, forms, Sessions.User),
            new KiralamaForm(films, rentals, reviews, Sessions.User, 1),
            new KullaniciProfil(rentals, reviews, forms, Sessions.User),
            new AdminPanel(films, forms, Sessions.Admin),
            new KiralananFilmlerPanel(rentals, films, Sessions.Admin),
            new DegerlendirmeForm(reviews, Sessions.User, reviews.Reviewable[0]),
            new YorumYonetimForm(reviews, Sessions.Admin),
            new IzlemeListesiForm(watchlist, forms, Sessions.User),
            new RaporlarForm(new ControlledReportService(), Sessions.Admin, TimeProvider.System),
            new OnerilerForm(new ControlledRecommendationService(), watchlist, forms, Sessions.User),
        ];

        try
        {
            Assert.Equal(
                ["Film Kiralama - Giriş", "Film Kiralama - Kayıt Ol", "Film Kiralama - Şifre Değiştir", "Film Kiralama - Katalog", "Film Kiralama - Kiralama Ekranı", "Film Kiralama - Kullanıcı Profili", "Film Kiralama - Yönetim Paneli", "Film Kiralama - Kiralanan Filmler", "Film Kiralama - Film Değerlendir", "Film Kiralama - Yorum Yönetimi", "Film Kiralama - İzleme Listem", "Film Kiralama - Raporlar", "Film Kiralama - Size Önerilenler"],
                all.Select(f => f.Text));
            Assert.All(all, f => Assert.NotNull(f.Icon));
        }
        finally
        {
            foreach (var form in all)
            {
                form.Dispose();
            }
        }
    });

    [Fact]
    public void Poster_NonWebAddress_ShowsThePlaceholderWithoutOpeningIt() => Run(() =>
    {
        using var picture = new PictureBox();

        picture.ShowPoster("\\\\sunucu\\pay\\afis.png");

        Assert.Same(AppAssets.Poster, picture.Image);
    });
}
