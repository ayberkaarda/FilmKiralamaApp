using System.Drawing.Drawing2D;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's film list: search by title, genre, year range, rating and availability page by page,
    /// rent a film, open the profile.
    /// </summary>
    public partial class AnaForm : Form, ISignOutPanel
    {
        private const int CardWidth = 240;
        private const int CardHeight = 480;
        private const int CardSpacing = 40;
        private const int RowSpacing = 30;
        private const int CardsPerRow = 4;

        private readonly IFilmService _films;
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _listing = new();
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly CatalogSearchBar _search;
        private readonly Pager _pager;
        private readonly Panel _scrollPanel;

        public AnaForm(IFilmService films, IWatchlistService watchlist, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _films = films;
            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            var header = BuildHeader();
            var filterPanel = new Panel
            {
                Size = new Size(ClientSize.Width, 60),
                Location = new Point(0, header.Bottom + 10),
                BackgroundImage = AppAssets.Background,
            };
            Controls.Add(filterPanel);
            _search = new CatalogSearchBar(new Point(20, 4), Color.White, availableOnly: true);
            filterPanel.Controls.Add(_search);
            _pager = new Pager(filterPanel, new Point(ClientSize.Width - 520, 22));

            // A new search starts on its first page; another page keeps the search.
            _search.Changed += async (_, _) => await this.RunAsync(() => LoadFilmsAsync(1));
            _pager.PageRequested += async (_, page) => await this.RunAsync(() => LoadFilmsAsync(page));

            _scrollPanel = new Panel
            {
                BackgroundImage = AppAssets.Background,
                BackgroundImageLayout = ImageLayout.Stretch,
                Location = new Point(0, filterPanel.Bottom + 10),
                AutoScroll = true,
            };
            _scrollPanel.Size = new Size(ClientSize.Width, ClientSize.Height - _scrollPanel.Top);
            Controls.Add(_scrollPanel);
        }

        public bool SignedOut { get; private set; }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
            var result = await _films.SearchCatalogAsync(_session, _search.Current with { Page = page });
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownPage = result;
            _pager.Show(result.Page, result.PageCount, result.TotalCount, "film");
            ShowFilms(result.Items);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private Panel BuildHeader()
        {
            var headerColor = ColorTranslator.FromHtml("#2a2a3b");
            var header = new Panel { Size = new Size(ClientSize.Width, 50), BackColor = headerColor };
            Controls.Add(header);

            header.Controls.Add(new Label
            {
                Text = "Merhaba, " + AppText.DisplayName(_session.UserName),
                ForeColor = Color.White,
                BackColor = headerColor,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 15),
                Size = new Size(500, 30),
            });

            var profile = new Button
            {
                Text = "Profil",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 230, 10),
                BackColor = Color.LightSeaGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            profile.Click += async (_, _) =>
            {
                using (var form = _forms.Create<KullaniciProfil>(_session))
                {
                    form.ShowDialog(this);
                }

                // A return in the profile puts a copy back on the shelf; a rating changes an average.
                await this.RunAsync(RefreshAsync);
            };
            header.Controls.Add(profile);

            var watchlist = new Button
            {
                Name = "btnIzlemeListesi",
                Text = "İzleme Listem",
                Size = new Size(120, 30),
                Location = new Point(header.Width - 360, 10),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            watchlist.Click += async (_, _) =>
            {
                using (var form = _forms.Create<IzlemeListesiForm>(_session))
                {
                    form.ShowDialog(this);
                }

                // A rental from the list takes a copy off the shelf.
                await this.RunAsync(RefreshAsync);
            };
            header.Controls.Add(watchlist);

            var recommendations = new Button
            {
                Name = "btnOneriler",
                Text = "Öneriler",
                Size = new Size(120, 30),
                Location = new Point(header.Width - 490, 10),
                BackColor = Color.DarkSlateBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            recommendations.Click += async (_, _) =>
            {
                using (var form = _forms.Create<OnerilerForm>(_session))
                {
                    form.ShowDialog(this);
                }

                // A rental from the recommendations takes a copy off the shelf.
                await this.RunAsync(RefreshAsync);
            };
            header.Controls.Add(recommendations);

            var signOut = new Button
            {
                Text = "Çıkış Yap",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 110, 10),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            signOut.Click += (_, _) =>
            {
                if (Prompts.Confirm("Çıkış yapmak istediğinizden emin misiniz?"))
                {
                    SignedOut = true;
                    Close();
                }
            };
            header.Controls.Add(signOut);
            return header;
        }

        private void ShowFilms(IReadOnlyList<CatalogItem> films)
        {
            var addButtons = new List<Button>();
            _scrollPanel.SuspendLayout();
            try
            {
                foreach (Control old in _scrollPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var rowWidth = (CardsPerRow * (CardWidth + CardSpacing)) - CardSpacing;
                var left = (ClientSize.Width - rowWidth) / 2;
                for (var i = 0; i < films.Count; i++)
                {
                    var card = BuildCard(films[i]);
                    addButtons.AddRange(card.Controls.OfType<Button>().Where(b => b.Name == "btnListeyeEkle"));
                    card.Location = new Point(
                        left + (i % CardsPerRow * (CardWidth + CardSpacing)),
                        10 + (i / CardsPerRow * (CardHeight + RowSpacing)) + _scrollPanel.AutoScrollPosition.Y);
                    _scrollPanel.Controls.Add(card);
                }
            }
            finally
            {
                _scrollPanel.ResumeLayout();
            }

            WatchlistButtons = addButtons;
        }

        private Panel BuildCard(CatalogItem item)
        {
            var film = item.Film;
            var card = new Panel
            {
                Size = new Size(CardWidth, CardHeight),
                BackColor = ColorTranslator.FromHtml("#2a2a3b"),
                Padding = new Padding(0),
            };
            card.Region = Rounded(card.Size, 20);

            var poster = new PictureBox
            {
                Size = new Size(240, 320),
                Location = new Point(0, 0),
                SizeMode = PictureBoxSizeMode.StretchImage,
            };
            poster.ShowPoster(film.ImageUrl);
            card.Controls.Add(poster);

            card.Controls.Add(new Label
            {
                Size = new Size(220, 92),
                Location = new Point(10, 324),
                Text = $"{film.Title}\nYönetmen: {film.Director}\n{film.Year} | {film.Genre} | {DisplayFormat.Money(film.DailyPrice)}\n"
                    + "Puan: " + ReviewDisplay.Summary(item.AverageRating, item.RatingCount),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                UseMnemonic = false,
            });

            var rent = new Button
            {
                Name = "btnKirala",
                Text = item.IsAvailable ? "Kirala" : "Stokta yok",
                Enabled = item.IsAvailable,
                Size = new Size(105, 36),
                Location = new Point(10, 420),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorTranslator.FromHtml("#3cb371"),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(0),
            };
            rent.FlatAppearance.BorderSize = 0;
            rent.Region = Rounded(rent.Size, 20);
            var filmId = film.Id;
            rent.Click += async (_, _) =>
            {
                bool rented;
                using (var form = _forms.Create<KiralamaForm>(_session, filmId))
                {
                    rented = form.ShowDialog(this) == DialogResult.OK;
                }

                if (rented)
                {
                    await this.RunAsync(() => LoadFilmsAsync(ShownPage?.Page ?? 1));
                }
            };
            card.Controls.Add(rent);

            var add = new Button
            {
                Name = "btnListeyeEkle",
                Text = "Listeye Ekle",
                Size = new Size(105, 36),
                Location = new Point(CardWidth - 115, 420),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            add.FlatAppearance.BorderSize = 0;
            add.Region = Rounded(add.Size, 20);
            add.Click += async (_, _) => await AddToWatchlistAsync(filmId);
            card.Controls.Add(add);
            return card;
        }

        private static Region Rounded(Size size, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(size.Width - radius, 0, radius, radius, 270, 90);
            path.AddArc(size.Width - radius, size.Height - radius, radius, radius, 0, 90);
            path.AddArc(0, size.Height - radius, radius, radius, 90, 90);
            return new Region(path);
        }
    }
}
