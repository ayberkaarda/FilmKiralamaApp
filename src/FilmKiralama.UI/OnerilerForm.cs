using System.ComponentModel;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Films recommended from the customer's own rentals (the genre rented most); rent one or put it on
    /// the watchlist.
    /// </summary>
    public sealed class OnerilerForm : AppDialog
    {
        private readonly IRecommendationService _recommendations;
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        private readonly Label lblOzet = FormLayout.Text(nameof(lblOzet));
        private readonly ThemedGrid dgvOneriler = new() { Name = nameof(dgvOneriler) };
        private readonly ThemedButton btnKirala = FormLayout.Button(nameof(btnKirala), "Kirala", ButtonKind.Primary, Glyphs.Cart);
        private readonly ThemedButton btnListeyeEkle = FormLayout.Button(nameof(btnListeyeEkle), "Listeye ekle", ButtonKind.Secondary, Glyphs.Watchlist);

        public OnerilerForm(IRecommendationService recommendations, IWatchlistService watchlist, IFormFactory forms, UserSession session)
            : base("Size Önerilenler", "En çok izlediğiniz türden, henüz kiralamadığınız filmler.", Glyphs.Like, new Size(960, 560), new Size(760, 460), modal: true)
        {
            ArgumentNullException.ThrowIfNull(recommendations);
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _recommendations = recommendations;
            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            BuildLayout();
            _write.HoldClosingOf(this);

            btnKirala.Enabled = false;
            btnListeyeEkle.Enabled = false;
            dgvOneriler.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };
            btnKirala.Click += async (_, _) => await RentSelectedAsync();
            btnListeyeEkle.Click += async (_, _) => await AddSelectedAsync();
        }

        internal IReadOnlyList<CatalogItem> ShownFilms { get; private set; } = [];

        internal CatalogItem? Selected => dgvOneriler.SelectedRows.Count == 1 && dgvOneriler.SelectedRows[0].DataBoundItem is FilmRowView row ? row.Item : null;

        /// <summary>Loads the recommendations; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            dgvOneriler.IsLoading = true;
            try
            {
                var films = await _recommendations.GetForMeAsync(_session);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownFilms = films;
                _binding = true;
                try
                {
                    dgvOneriler.DataSource = films.Select(f => new FilmRowView(f)).ToList();
                    dgvOneriler.ClearSelection();
                }
                finally
                {
                    _binding = false;
                }

                lblOzet.Text = films.Count == 0
                    ? "Henüz öneri yok: birkaç film kiraladıkça en çok izlediğiniz türden filmler burada görünür."
                    : "En çok kiraladığınız türden, henüz kiralamadığınız ve şu anda kiralanabilen filmler:";
                UpdateButtons();
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dgvOneriler.IsLoading = false;
                }
            }
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
                btnListeyeEkle,
                btnKirala);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadAsync);
        }

        private async Task RentSelectedAsync()
        {
            if (Selected is not { } item || _write.IsRunning)
            {
                return;
            }

            RentalReceipt? receipt;
            using (var form = _forms.Create<KiralamaForm>(_session, item.Film.Id))
            {
                receipt = form.ShowDialog(this) == DialogResult.OK ? form.Receipt : null;
            }

            if (receipt is not null && !IsDisposed)
            {
                await this.RunAsync(LoadAsync);
                ShowStatus(StatusKind.Success, KiralamaForm.Confirmation(item.Film.Title, receipt));
            }
        }

        private void UpdateButtons()
        {
            var selected = Selected is not null;
            btnKirala.Enabled = selected;
            btnListeyeEkle.Enabled = selected;
        }

        private void BuildLayout()
        {
            Name = nameof(OnerilerForm);
            lblOzet.Font = Theme.Fonts.BodyStrong;
            lblOzet.Margin = new Padding(0, 0, 0, Spacing.M);
            dgvOneriler.EmptyText = "Öneri için önce birkaç film kiralayın.";
            dgvOneriler.ColumnWeights = new Dictionary<string, float>
            {
                [nameof(FilmRowView.Title)] = 200,
                [nameof(FilmRowView.Year)] = 60,
                [nameof(FilmRowView.Director)] = 140,
                [nameof(FilmRowView.Price)] = 120,
            };
            var card = FormLayout.Card("listCard", null, dgvOneriler);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("listPage", (lblOzet, false), (card, true)));

            AcceptButton = btnKirala;
            SetFooterNote("Öneriler kiraladığınız filmlerden hesaplanır; en çok 8 film gösterilir.");
            SetFooterButtons(btnKirala, btnListeyeEkle, CreateCloseButton("btnKapat"));
            FinishLayout();
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
            public string Rating => FilmText.Rating(Item.AverageRating, Item.RatingCount);

            [DisplayName("Günlük Ücret")]
            public string Price => DisplayFormat.Money(Item.Film.DailyPrice);
        }
    }
}
