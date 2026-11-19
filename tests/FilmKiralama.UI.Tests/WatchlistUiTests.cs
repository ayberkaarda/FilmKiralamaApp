using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>The watchlist on the screens: one change at a time, lists bound without a selection.</summary>
public sealed class WatchlistUiTests
{
    [Fact]
    public void Catalog_SecondAddWhilePending_IsIgnoredAndEveryAddButtonWaits() => Run(() =>
    {
        var films = new ControlledFilmService();
        var watchlist = new ControlledWatchlistService();
        var prompts = new RecordingPrompts();
        using var form = new AnaForm(films, watchlist, new NoFormsFactory(), Sessions.User) { Prompts = prompts };
        _ = form.LoadFilmsAsync();
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page(films.Films));
        Assert.Equal(2, form.WatchlistButtons.Count);

        var first = form.AddToWatchlistAsync(1);
        var second = form.AddToWatchlistAsync(2);

        Assert.False(second.Result);
        Assert.Equal([1], watchlist.Added);
        Assert.All(form.WatchlistButtons, b => Assert.False(b.Enabled));

        watchlist.CompleteAdd(added: false);
        Assert.True(first.Result);
        Assert.Equal(["Film zaten izleme listenizde."], prompts.Messages);
        Assert.All(form.WatchlistButtons, b => Assert.True(b.Enabled));
    });

    [Fact]
    public void List_IsBoundWithoutASelectionAndRentsOnlyAvailableFilms() => Run(() =>
    {
        var watchlist = new ControlledWatchlistService();
        using var form = new IzlemeListesiForm(watchlist, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvListe");
        _ = grid.Handle;

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Assert.Equal(2, grid.Rows.Count);
        Assert.Empty(grid.SelectedRows);
        Assert.False(Find<Button>(form, "btnKirala").Enabled);
        Assert.False(Find<Button>(form, "btnCikar").Enabled);
        Assert.Equal("Listenizde 2 film var; 1 tanesi şu anda kiralanabilir.", Find<Label>(form, "lblOzet").Text);

        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.Rows[1].Selected = true;
        Assert.False(Find<Button>(form, "btnKirala").Enabled);
        Assert.True(Find<Button>(form, "btnCikar").Enabled);

        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Rows[0].Selected = true;
        Assert.True(Find<Button>(form, "btnKirala").Enabled);
    });

    [Fact]
    public void List_SecondRemoveWhilePending_IsIgnored() => Run(() =>
    {
        var watchlist = new ControlledWatchlistService();
        using var form = new IzlemeListesiForm(watchlist, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvListe");
        _ = grid.Handle;
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Rows[0].Selected = true;

        var first = form.RemoveAsync();
        var second = form.RemoveAsync();

        Assert.False(second.Result);
        Assert.Equal([5], watchlist.Removed);
        Assert.False(Find<Button>(form, "btnCikar").Enabled);

        watchlist.Entries.RemoveAt(0);
        watchlist.CompleteRemove();
        Assert.True(first.Result);
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Empty(grid.SelectedRows);
    });

    /// <summary>A film unlisted since it was added shows its title and "Kaldırıldı" only, and can be removed.</summary>
    [Fact]
    public void List_UnlistedFilm_ShowsItsTitleOnly() => Run(() =>
    {
        var watchlist = new ControlledWatchlistService();
        watchlist.Entries.Clear();
        watchlist.Entries.Add(new(7, new CatalogItem(new Film { Id = 9, Title = "Eski Film", IsListed = false }, null, 0), new DateOnly(2026, 3, 1)));
        using var form = new IzlemeListesiForm(watchlist, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvListe");
        _ = grid.Handle;

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        var cells = grid.Rows[0].Cells.Cast<DataGridViewCell>().ToDictionary(c => c.OwningColumn!.HeaderText, c => c.FormattedValue?.ToString() ?? string.Empty);
        Assert.Equal("Eski Film", cells["Film"]);
        Assert.Equal("Kaldırıldı", cells["Durum"]);
        Assert.Equal([string.Empty, string.Empty, string.Empty, string.Empty], [cells["Tür"], cells["Yıl"], cells["Puan"], cells["Günlük Ücret"]]);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Rows[0].Selected = true;
        Assert.False(Find<Button>(form, "btnKirala").Enabled);
        Assert.True(Find<Button>(form, "btnCikar").Enabled);
    });

    [Fact]
    public void EmptyList_SaysHowToAddFilms() => Run(() =>
    {
        var watchlist = new ControlledWatchlistService();
        watchlist.Entries.Clear();
        using var form = new IzlemeListesiForm(watchlist, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Assert.StartsWith("İzleme listeniz boş.", Find<Label>(form, "lblOzet").Text, StringComparison.Ordinal);
    });
}
