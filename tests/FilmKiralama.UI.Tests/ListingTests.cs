using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>Filter loads apply only the latest answer, and filling or binding lists is never a user choice.</summary>
public sealed class ListingTests
{
    [Fact]
    public void Catalog_AnswersOutOfOrder_ShowOnlyTheLatestFilter() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var refresh = form.RefreshAsync();
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films));
        Assert.True(refresh.IsCompletedSuccessfully);

        var genre = Find<ComboBox>(form, "turCombo");
        genre.SelectedIndex = 1; // "Dram"
        genre.SelectedIndex = 2; // "Gerilim"
        Assert.Equal([null, "Dram", "Gerilim"], films.CatalogRequests.Select(r => r.Query.Genre));

        // The newer answer arrives first, then the stale one.
        films.CatalogRequests[2].Answer.SetResult(ControlledFilmService.Page([films.Films[0]]));
        films.CatalogRequests[1].Answer.SetResult(ControlledFilmService.Page([films.Films[1]]));

        Assert.Equal([1], form.ShownFilms.Select(f => f.Id));
    });

    [Fact]
    public void Catalog_FillingTheFilters_StartsNoLoadAndKeepsTheChoice() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        _ = form.RefreshAsync();
        Assert.Single(films.CatalogRequests);
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films));

        Find<ComboBox>(form, "turCombo").SelectedIndex = 2;
        films.CatalogRequests[1].Answer.SetResult(ControlledFilmService.Page([]));
        _ = form.RefreshAsync();

        Assert.Equal(3, films.CatalogRequests.Count);
        Assert.Equal("Gerilim", films.CatalogRequests[2].Query.Genre);
    });

    [Fact]
    public void AdminFilms_BindingTheGrid_SelectsNothing() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dataGridView1");
        _ = grid.Handle;

        Assert.True(form.LoadFilmsAsync().IsCompletedSuccessfully);

        Assert.Equal(2, grid.Rows.Count);
        Assert.Empty(grid.SelectedRows);
        Assert.Null(form.SelectedFilmId);
        Assert.False(Find<Button>(form, "btnGuncelle").Enabled);
        Assert.Empty(Find<TextBox>(form, "txtAd").Text);

        // A click on a row is a choice; it fills the form.
        form.SelectFilm(films.Films[1]);
        Assert.Equal("Son Vapur", Find<TextBox>(form, "txtAd").Text);
        Assert.Equal("12,5", Find<TextBox>(form, "txtUcret").Text);
        Assert.True(Find<Button>(form, "btnGuncelle").Enabled);
    });

    [Fact]
    public void AdminFilms_FirstAddWithASelection_OnlyClearsTheForm() => Run(() =>
    {
        var films = new ControlledFilmService();
        var prompts = new RecordingPrompts();
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = prompts };
        form.SelectFilm(films.Films[0]);

        Assert.True(form.AddAsync().IsCompletedSuccessfully);

        Assert.Equal(0, films.AddCalls);
        Assert.Null(form.SelectedFilmId);
        Assert.Empty(Find<TextBox>(form, "txtAd").Text);
        Assert.Single(prompts.Messages);
    });

    [Fact]
    public void FilmRow_ShowsTheOriginalColumnNamesAndDerivedStatus()
    {
        var film = ControlledFilmService.Film(1, "Sisli Liman", "Gerilim");
        var headers = System.ComponentModel.TypeDescriptor.GetProperties(typeof(FilmRow))
            .Cast<System.ComponentModel.PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .Select(p => p.DisplayName);

        Assert.Equal(["FilmID", "Ad", "Tur", "Yil", "Yonetmen", "GunlukUcret", "Adet", "MevcutAdet", "Durum", "Image", "Hakkinda"], headers);
        Assert.Equal("Müsait", FilmRow.Describe(film));
        film.AvailableCopies = 0;
        Assert.Equal("Kirada", FilmRow.Describe(film));
        film.IsListed = false;
        Assert.Equal("Kiralamaya kapalı", FilmRow.Describe(film));
    }

    [Fact]
    public void UserRow_NeverShowsAPassword()
    {
        var names = System.ComponentModel.TypeDescriptor.GetProperties(typeof(UserRow))
            .Cast<System.ComponentModel.PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .Select(p => p.DisplayName)
            .ToList();

        Assert.Equal(["KullaniciID", "KullaniciAdi", "Eposta", "Telefon", "Rol"], names);
        Assert.Equal("Müşteri", new UserRow(new UserSummary(2, "ornek", null, null, UserRole.User)).Role);
    }

    [Theory]
    [InlineData("5", "5")]
    [InlineData("5551", "555 1")]
    [InlineData("5551234", "555 123 4")]
    [InlineData("555 123 45678", "555 123 4567")]
    public void FormatPhone_GroupsTheDigitsAsTheOriginalForm(string typed, string shown)
    {
        Assert.Equal(shown, UiExtensions.FormatPhone(typed));
    }

    [Theory]
    [InlineData("ornek", "Ornek")]
    [InlineData("İREM", "İrem")]
    [InlineData("ılgaz", "Ilgaz")]
    public void DisplayName_CapitalizesTheFirstLetterTheTurkishWay(string name, string shown)
    {
        Assert.Equal(shown, AppText.DisplayName(name));
    }
}
