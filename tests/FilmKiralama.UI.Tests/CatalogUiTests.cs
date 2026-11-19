using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>The catalog search on the customer's and the admin's screens: queries, pages and latest answers.</summary>
public sealed class CatalogUiTests
{
    /// <summary>The first search asks for available films on page one; typing a title starts again on page one.</summary>
    [Fact]
    public void Catalog_SearchFields_BuildTheQuery() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        _ = form.RefreshAsync();
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films, total: 30));

        Assert.Equal(new CatalogQuery(AvailableOnly: true), films.CatalogRequests[0].Query);
        Assert.Equal("Sayfa 1 / 3 (30 film)", Find<Label>(form, "lblSayfa").Text);
        Assert.False(Find<Button>(form, "btnOncekiSayfa").Enabled);
        Assert.True(Find<Button>(form, "btnSonrakiSayfa").Enabled);

        _ = form.LoadFilmsAsync(2);
        Find<ComboBox>(form, "puanCombo").SelectedIndex = 2;   // 4+
        Find<ComboBox>(form, "yilBaslangicCombo").SelectedIndex = 1; // 2015
        Find<CheckBox>(form, "chkMusait").Checked = false;
        Find<TextBox>(form, "txtAra").Text = "  Liman ";

        Assert.Equal(2, films.CatalogRequests[1].Query.Page);
        Assert.Equal(
            new CatalogQuery(Title: "Liman", YearFrom: 2015, MinRating: 4, AvailableOnly: false, Page: 1),
            films.CatalogRequests[^1].Query);
    });

    /// <summary>Answers of two pages arriving out of order: the later request wins, also for the pager.</summary>
    [Fact]
    public void Catalog_PageAnswersOutOfOrder_ShowOnlyTheLatest() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };

        var second = form.LoadFilmsAsync(2);
        var third = form.LoadFilmsAsync(3);
        films.CatalogRequests[1].Answer.SetResult(ControlledFilmService.Page([films.Films[1]], page: 3, total: 30));
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page([films.Films[0]], page: 2, total: 30));

        Assert.True(second.IsCompletedSuccessfully && third.IsCompletedSuccessfully);
        Assert.Equal([2], form.ShownFilms.Select(f => f.Id));
        Assert.Equal("Sayfa 3 / 3 (30 film)", Find<Label>(form, "lblSayfa").Text);
        Assert.False(Find<Button>(form, "btnSonrakiSayfa").Enabled);
    });

    /// <summary>A film without a copy on the shelf is shown, but cannot be rented.</summary>
    [Fact]
    public void Catalog_FilmWithoutACopy_HasNoRentButton() => Run(() =>
    {
        var films = new ControlledFilmService();
        films.Films[1].AvailableCopies = 0;
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };

        _ = form.LoadFilmsAsync();
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films));

        var buttons = form.Controls.Find("btnKirala", searchAllChildren: true).OfType<Button>().ToList();
        Assert.Equal(["Kirala", "Stokta yok"], buttons.Select(b => b.Text));
        Assert.Equal([true, false], buttons.Select(b => b.Enabled));
    });

    [Fact]
    public void AdminFilms_SearchAnswersOutOfOrder_ShowOnlyTheLatestAndSelectNothing() => Run(() =>
    {
        var films = new ControlledFilmService { HoldAdminSearches = true };
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dataGridView1");
        _ = grid.Handle;

        _ = form.LoadFilmsAsync();
        Find<TextBox>(form, "txtAra").Text = "Son";
        films.AdminSearches[1].Answer.SetResult(ControlledFilmService.Page([films.Films[1]], pageSize: 50));
        films.AdminSearches[0].Answer.SetResult(ControlledFilmService.Page(films.Films, pageSize: 50));

        Assert.Equal(new CatalogQuery(Title: "Son", PageSize: CatalogQuery.MaxPageSize), films.AdminSearches[1].Query);
        Assert.Equal([2], form.ShownFilms.Select(f => f.Id));
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Empty(grid.SelectedRows);
        Assert.Null(form.SelectedFilmId);
        Assert.Equal("Sayfa 1 / 1 (1 film)", Find<Label>(form, "lblSayfa").Text);
    });

    /// <summary>Filling the genre and year choices starts no search and keeps what was chosen.</summary>
    [Fact]
    public void SearchBar_FillingTheChoices_StartsNothingAndKeepsTheChoice() => Run(() =>
    {
        var films = new ControlledFilmService { HoldAdminSearches = true };
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        form.Search.Fill(films.Options);
        Assert.Empty(films.AdminSearches);

        Find<ComboBox>(form, "yilBitisCombo").SelectedIndex = 2; // 2019
        form.Search.Fill(films.Options with { Years = [2021, 2019, 2010] });

        Assert.Single(films.AdminSearches);
        Assert.Equal(2019, form.Search.Current.YearTo);
        Assert.Equal(["Tümü", "2010", "2019", "2021"], Find<ComboBox>(form, "yilBitisCombo").Items.Cast<object>().Select(i => i.ToString()));
    });
}
