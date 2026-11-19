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
            ["FilmKiralama.UI.Assets.app.ico", "FilmKiralama.UI.Assets.background.png", "FilmKiralama.UI.Assets.hero.png", "FilmKiralama.UI.Assets.logo.png", "FilmKiralama.UI.Assets.poster.png"],
            resources.Where(r => r.Contains(".Assets.", StringComparison.Ordinal)));
        Assert.DoesNotContain(resources, r => r.EndsWith(".resources", StringComparison.Ordinal) && !r.Contains("ReaLTaiizor", StringComparison.Ordinal));
        Assert.Equal(new Size(545, 737), AppAssets.Hero.Size);
        Assert.Equal(new Size(240, 320), AppAssets.Poster.Size);
        Assert.Equal(256, AppAssets.Logo.Width);
        Assert.NotNull(AppAssets.Icon);
    }

    [Fact]
    public void Forms_HaveNeutralTurkishTitles() => Run(() =>
    {
        var films = new ControlledFilmService();
        var rentals = new ControlledRentalService();
        var forms = new NoFormsFactory();
        Form[] all =
        [
            new LoginForm(new PendingAuthService(), forms),
            new RegisterForm(new PendingAuthService()),
            new SifreDegistirmeForm(new PendingAuthService(), Sessions.User),
            new AnaForm(films, forms, Sessions.User),
            new KiralamaForm(films, rentals, Sessions.User, 1),
            new KullaniciProfil(rentals, forms, Sessions.User),
            new AdminPanel(films, forms, Sessions.Admin),
            new KiralananFilmlerPanel(rentals, films, Sessions.Admin),
        ];

        try
        {
            Assert.Equal(
                ["Film Kiralama", "Kayıt Ol", "Şifre Değiştir", "Film Kirala", "Kiralama Ekranı", "Kullanıcı Profili", "Yönetim Paneli", "Kiralanan Filmler"],
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
