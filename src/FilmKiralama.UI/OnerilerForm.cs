using System.ComponentModel;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Films recommended from the customer's own rentals (the genre rented most); rent one or put it on
    /// the watchlist.
    /// </summary>
    public sealed class OnerilerForm : Form
    {
        private readonly IRecommendationService _recommendations;
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly DataGridView _grid;
        private readonly Label _summary;
        private readonly Button _rent;
        private readonly Button _add;
        private bool _binding;

        public OnerilerForm(IRecommendationService recommendations, IWatchlistService watchlist, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(recommendations);
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _recommendations = recommendations;
            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            Text = "Size Önerilenler";
            Icon = AppAssets.Icon;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(900, 480);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _summary = new Label { Name = "lblOzet", Location = new Point(16, 16), Size = new Size(860, 26), Font = new Font("Segoe UI", 11, FontStyle.Bold), UseMnemonic = false };
            _grid = new DataGridView
            {
                Name = "dgvOneriler",
                Location = new Point(16, 50),
                Size = new Size(868, 360),
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            _grid.UseReadOnlyList();
            _grid.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };
            _rent = new Button { Name = "btnKirala", Text = "Kirala", Location = new Point(574, 426), Size = new Size(150, 36), Enabled = false };
            _add = new Button { Name = "btnListeyeEkle", Text = "Listeye Ekle", Location = new Point(734, 426), Size = new Size(150, 36), Enabled = false };
            _rent.Click += async (_, _) => await RentSelectedAsync();
            _add.Click += async (_, _) => await AddSelectedAsync();
            Controls.AddRange([_summary, _grid, _rent, _add]);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        internal IReadOnlyList<CatalogItem> ShownFilms { get; private set; } = [];

        internal CatalogItem? Selected => _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].DataBoundItem is FilmRowView row ? row.Item : null;

        /// <summary>Loads the recommendations; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            var films = await _recommendations.GetForMeAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownFilms = films;
            _binding = true;
            try
            {
                _grid.DataSource = films.Select(f => new FilmRowView(f)).ToList();
                _grid.ClearSelection();
            }
            finally
            {
                _binding = false;
            }

            _summary.Text = films.Count == 0
                ? "Henüz öneri yok: birkaç film kiraladıkça en çok izlediğiniz türden filmler burada görünür."
                : "En çok kiraladığınız türden, henüz kiralamadığınız ve şu anda kiralanabilen filmler:";
            UpdateButtons();
        }

        /// <summary>Puts the selected film on the watchlist; ignored while another addition is running.</summary>
        internal Task<bool> AddSelectedAsync() =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (Selected is not { } item)
                    {
                        Prompts.Warn("Lütfen bir film seçin.");
                        return;
                    }

                    var added = await _watchlist.AddAsync(_session, item.Film.Id);
                    Prompts.Inform(added ? "Film izleme listenize eklendi." : "Film zaten izleme listenizde.");
                },
                _add,
                _rent);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadAsync);
        }

        private async Task RentSelectedAsync()
        {
            if (Selected is not { } item)
            {
                return;
            }

            bool rented;
            using (var form = _forms.Create<KiralamaForm>(_session, item.Film.Id))
            {
                rented = form.ShowDialog(this) == DialogResult.OK;
            }

            if (rented)
            {
                await this.RunAsync(LoadAsync);
            }
        }

        private void UpdateButtons()
        {
            var selected = Selected is not null;
            _rent.Enabled = selected;
            _add.Enabled = selected;
        }

        /// <summary>A recommended film as a row; texts are plain text in cells.</summary>
        private sealed class FilmRowView(CatalogItem item)
        {
            [Browsable(false)]
            public CatalogItem Item { get; } = item;

            [DisplayName("Film")]
            public string Title => Item.Film.Title;

            [DisplayName("Tür")]
            public string Genre => Item.Film.Genre;

            [DisplayName("Yıl")]
            public int Year => Item.Film.Year;

            [DisplayName("Yönetmen")]
            public string Director => Item.Film.Director;

            [DisplayName("Puan")]
            public string Rating => ReviewDisplay.Summary(Item.AverageRating, Item.RatingCount);

            [DisplayName("Günlük Ücret")]
            public string Price => DisplayFormat.Money(Item.Film.DailyPrice);
        }
    }
}
