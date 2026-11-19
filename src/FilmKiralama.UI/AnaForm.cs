using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's catalog: poster cards of the films found by title, genre, year range, rating and
    /// availability, page by page; rent a film or put it on the watchlist, and open the recommendations, the
    /// watchlist and the profile. Signing out returns to the sign-in.
    /// </summary>
    public sealed class AnaForm : AppDialog, ISignOutPanel
    {
        private readonly IFilmService _films;
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _listing = new();
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly CatalogSearchBar _search = new(availableOnly: true);
        private readonly Pager _pager = new();
        private readonly CardGrid _grid = new() { Name = "pnlFilmler" };
        private readonly ToolTip _tips = new();

        private readonly ThemedButton btnOneriler = FormLayout.Button(nameof(btnOneriler), "Öneriler", ButtonKind.Secondary, Glyphs.Like);
        private readonly ThemedButton btnIzlemeListesi = FormLayout.Button(nameof(btnIzlemeListesi), "İzleme listem", ButtonKind.Secondary, Glyphs.Watchlist);
        private readonly ThemedButton btnProfil = FormLayout.Button(nameof(btnProfil), "Profilim", ButtonKind.Secondary, Glyphs.Person);
        private readonly ThemedButton btnCikis = FormLayout.Button(nameof(btnCikis), "Çıkış yap", ButtonKind.Secondary, Glyphs.SignOut);

        public AnaForm(IFilmService films, IWatchlistService watchlist, IFormFactory forms, UserSession session)
            : base("Katalog", "Merhaba, " + AppText.DisplayName(session?.UserName ?? string.Empty) + ". Bugün ne izlemek istersiniz?", Glyphs.Video, new Size(1200, 780), new Size(860, 600))
        {
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _films = films;
            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            BuildLayout();

            // A new search starts on its first page; another page keeps the search.
            _search.Changed += async (_, _) => await this.RunAsync(() => LoadFilmsAsync(1));
            _pager.PageRequested += async (_, page) => await this.RunAsync(() => LoadFilmsAsync(page));
            btnOneriler.Click += async (_, _) => await OpenAndRefreshAsync<OnerilerForm>();
            btnIzlemeListesi.Click += async (_, _) => await OpenAndRefreshAsync<IzlemeListesiForm>();
            btnProfil.Click += async (_, _) => await OpenAndRefreshAsync<KullaniciProfil>();
            btnCikis.Click += (_, _) => SignOut();
        }

        public bool SignedOut { get; private set; }

        /// <summary>Shows the posters of the cards; replaced by the screenshot tool so no download starts.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PosterLoader Posters { get; set; } = PosterLoader.Default;

        /// <summary>The film cards shown now, for tests.</summary>
        internal IReadOnlyList<Film> ShownFilms => ShownPage?.Items.Select(i => i.Film).ToList() ?? [];

        /// <summary>The page of the catalog shown now.</summary>
        internal PagedResult<CatalogItem>? ShownPage { get; private set; }

        internal CatalogSearchBar Search => _search;

        internal Pager Pager => _pager;

        /// <summary>The "add to watchlist" buttons of the cards shown now.</summary>
        internal IReadOnlyList<Button> WatchlistButtons { get; private set; } = [];

        /// <summary>
        /// Adds a film to the watchlist; ignored while another addition is running, and every add button
        /// stays disabled meanwhile.
        /// </summary>
        internal Task<bool> AddToWatchlistAsync(int filmId) =>
            this.RunExclusiveAsync(
                _listing,
                async () =>
                {
                    var added = await _watchlist.AddAsync(_session, filmId);
                    Prompts.Inform(added ? "Film izleme listenize eklendi." : "Film zaten izleme listenizde.");
                },
                [.. WatchlistButtons]);

        /// <summary>Loads the search choices and the shown page again.</summary>
        internal async Task RefreshAsync()
        {
            _search.Fill(await _films.GetFilterOptionsAsync(_session));
            await LoadFilmsAsync(ShownPage?.Page ?? 1);
        }

        /// <summary>Loads one page of the current search; only the latest request is shown.</summary>
        internal async Task LoadFilmsAsync(int page = 1)
        {
            var request = _loads.Next();
            _grid.IsLoading = true;
            try
            {
                var result = await _films.SearchCatalogAsync(_session, _search.Current with { Page = page });
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownPage = result;
                _pager.Show(result.Page, result.PageCount, result.TotalCount, "film");
                ShowFilms(result.Items);
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    _grid.IsLoading = false;
                }
            }
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
            }

            base.Dispose(disposing);
        }

        private void SignOut()
        {
            if (Prompts.Confirm("Çıkış yapmak istediğinizden emin misiniz?"))
            {
                SignedOut = true;
                Close();
            }
        }

        /// <summary>Opens a screen of the session; a return, a rating or a rental there changes the catalog.</summary>
        private async Task OpenAndRefreshAsync<T>()
            where T : Form
        {
            using (var form = _forms.Create<T>(_session))
            {
                form.ShowDialog(this);
            }

            if (!IsDisposed)
            {
                await this.RunAsync(RefreshAsync);
            }
        }

        private async Task RentAsync(CatalogItem item)
        {
            RentalReceipt? receipt;
            using (var form = _forms.Create<KiralamaForm>(_session, item.Film.Id))
            {
                receipt = form.ShowDialog(this) == DialogResult.OK ? form.Receipt : null;
            }

            if (receipt is not null && !IsDisposed)
            {
                ShowStatus(StatusKind.Success, KiralamaForm.Confirmation(item.Film.Title, receipt));
                await this.RunAsync(() => LoadFilmsAsync(ShownPage?.Page ?? 1));
            }
        }

        private void ShowFilms(IReadOnlyList<CatalogItem> films)
        {
            var cards = new List<PosterCard>();
            foreach (var item in films)
            {
                var card = new PosterCard(item);
                var current = item;
                card.Rent.Click += async (_, _) => await RentAsync(current);
                card.AddToList.Click += async (_, _) => await AddToWatchlistAsync(current.Film.Id);
                _tips.SetToolTip(card.AddToList, "İzleme listesine ekle");
                Posters.Show(card.Poster, item.Film.ImageUrl);
                cards.Add(card);
            }

            _grid.SetItems(cards);
            WatchlistButtons = [.. cards.Select(c => c.AddToList)];
        }

        private void BuildLayout()
        {
            Name = nameof(AnaForm);
            var toolbar = FormLayout.Bar(
                "toolbar",
                (btnOneriler, false),
                (btnIzlemeListesi, false),
                (btnProfil, false),
                (FormLayout.Spacer(), true),
                (_pager, false));
            toolbar.Margin = new Padding(0, 0, 0, Spacing.M);
            btnOneriler.Margin = new Padding(0, 0, Spacing.S, 0);
            btnIzlemeListesi.Margin = new Padding(0, 0, Spacing.S, 0);
            btnProfil.Margin = new Padding(0, 0, Spacing.L, 0);

            var searchCard = FormLayout.Card("searchCard", null, _search, fill: false);
            searchCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, Spacing.XS);
            searchCard.Margin = new Padding(0, 0, 0, Spacing.L);

            _grid.EmptyText = "Aramanıza uyan film bulunamadı. Filtreleri gevşetin ya da \"Yalnızca müsait\" seçeneğini kapatın.";
            _grid.MinimumSize = new Size(0, 280);
            Content.Controls.Add(FormLayout.Page("catalogPage", (toolbar, false), (searchCard, false), (_grid, true)));

            CancelButton = btnCikis;
            SetFooterNote("Kirala ile kiralama ekranı açılır; yıldızlı liste düğmesi filmi izleme listenize ekler.");
            SetFooterButtons(btnCikis);
            FinishLayout();
        }
    }
}
