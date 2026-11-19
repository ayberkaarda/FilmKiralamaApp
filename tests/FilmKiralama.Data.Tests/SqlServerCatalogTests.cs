using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>
/// The catalog search on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>), where [ ]
/// is a character class in LIKE and the Turkish collation decides the order.
/// </summary>
public sealed class SqlServerCatalogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_LikeCharactersAndRatings_BehaveAsOnEveryProvider()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        foreach (var title in new[] { "[A-Z] Harfler", "B Harfler", "100% Gerçek", "Tam 1000 Film", "Alt_Çizgi", "AltXÇizgi" })
        {
            await AddFilmAsync(provider, title, copies: 1);
        }

        var films = provider.GetRequiredService<IFilmService>();
        async Task<string[]> TitlesAsync(CatalogQuery query) =>
            [.. (await films.SearchCatalogAsync(User(SeedUserId), query, Ct)).Items.Select(i => i.Film.Title)];

        Assert.Equal(["[A-Z] Harfler"], await TitlesAsync(new CatalogQuery(Title: "[A-Z]")));
        Assert.Equal(["100% Gerçek"], await TitlesAsync(new CatalogQuery(Title: "100%")));
        Assert.Equal(["Alt_Çizgi"], await TitlesAsync(new CatalogQuery(Title: "t_Ç")));

        // Ratings 4, 4 and 3 show as 3.7: three stars and more, not four.
        var rated = await AddFilmAsync(provider, "Puanlı", copies: 3);
        await database.ExecuteAsync(
            """
            INSERT dbo.Kullanicilar (KullaniciAdi, Sifre, SifreBicimi, Rol, BasarisizGiris) VALUES (N'p1', N'x', 0, N'Kullanici', 0), (N'p2', N'x', 0, N'Kullanici', 0), (N'p3', N'x', 0, N'Kullanici', 0);
            INSERT dbo.Degerlendirmeler (KullaniciID, FilmID, Puan, Gizli, GuncellemeTarihi)
                SELECT KullaniciID, @film, CASE KullaniciAdi WHEN N'p3' THEN 3 ELSE 4 END, 0, SYSUTCDATETIME() FROM dbo.Kullanicilar WHERE KullaniciAdi IN (N'p1', N'p2', N'p3');
            """,
            Ct,
            ("@film", rated));

        var three = await films.SearchCatalogAsync(User(SeedUserId), new CatalogQuery(Title: "Puanlı", MinRating: 3), Ct);
        Assert.Equal((3.7m, 3), (three.Items.Single().AverageRating, three.Items.Single().RatingCount));
        Assert.Empty(await TitlesAsync(new CatalogQuery(Title: "Puanlı", MinRating: 4)));
    }
}
