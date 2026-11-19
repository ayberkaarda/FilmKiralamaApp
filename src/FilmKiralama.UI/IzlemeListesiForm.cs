using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>The customer's watchlist: which films are available now, rent one or take it off the list.</summary>
    public sealed class IzlemeListesiForm : AppDialog
    {
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        private readonly Label lblOzet = FormLayout.Text(nameof(lblOzet));
        private readonly ThemedGrid dgvListe = new() { Name = nameof(dgvListe) };
        private readonly ThemedButton btnKirala = FormLayout.Button(nameof(btnKirala), "Kirala", ButtonKind.Primary, Glyphs.Cart);
        private readonly ThemedButton btnCikar = FormLayout.Button(nameof(btnCikar), "Listeden çıkar", ButtonKind.Secondary, Glyphs.Delete);

        public IzlemeListesiForm(IWatchlistService watchlist, IFormFactory forms, UserSession session)
            : base("İzleme Listem", "Beğendiğiniz filmler; şu anda müsait olanları buradan kiralayın.", Glyphs.Watchlist, new Size(960, 600), new Size(760, 480), modal: true)
        {
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            BuildLayout();
            _write.HoldClosingOf(this);

            btnKirala.Enabled = false;
            btnCikar.Enabled = false;
            dgvListe.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };
            dgvListe.CellDoubleClick += async (_, e) =>
            {
                if (e.RowIndex >= 0)
                {
                    await RentSelectedAsync();
                }
            };
            btnKirala.Click += async (_, _) => await RentSelectedAsync();
            btnCikar.Click += async (_, _) => await RemoveAsync();
        }

        /// <summary>The list as shown now.</summary>
        internal IReadOnlyList<WatchlistView> ShownEntries { get; private set; } = [];

        /// <summary>The entry whose row is selected, or <see langword="null"/>.</summary>
        internal WatchlistView? Selected => dgvListe.SelectedRows.Count == 1 && dgvListe.SelectedRows[0].DataBoundItem is EntryRow row ? row.Entry : null;

        /// <summary>Loads the list; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            dgvListe.IsLoading = true;
            try
            {
                var entries = await _watchlist.GetMineAsync(_session);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownEntries = entries;
                _binding = true;
                try
                {
                    dgvListe.DataSource = entries.Select(e => new EntryRow(e)).ToList();
                    dgvListe.ClearSelection();
                }
                finally
                {
                    _binding = false;
                }

                var available = entries.Count(e => e.Item.IsAvailable);
                lblOzet.Text = entries.Count == 0
                    ? "İzleme listeniz boş. Katalogdaki filmleri \"Listeye Ekle\" ile ekleyebilirsiniz."
                    : string.Create(CultureInfo.InvariantCulture, $"Listenizde {entries.Count} film var; {available} tanesi şu anda kiralanabilir.");
                UpdateButtons();
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dgvListe.IsLoading = false;
                }
            }
        }

        /// <summary>Takes the selected film off the list; ignored while another change is running.</summary>
        internal Task<bool> RemoveAsync() =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (Selected is not { } entry)
                    {
                        Prompts.Warn("Lütfen listeden bir film seçin.");
                        return;
                    }

                    await _watchlist.RemoveAsync(_session, entry.EntryId);
                    await LoadAsync();
                    Prompts.Inform("\"" + entry.Item.Film.Title + "\" listenizden çıkarıldı.");
                },
                btnKirala,
                btnCikar);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadAsync);
        }

        /// <summary>Opens the rental of the selected film when it can be rented now; a rental takes it off the list.</summary>
        private async Task RentSelectedAsync()
        {
            if (Selected is not { Item.IsAvailable: true } entry || _write.IsRunning)
            {
                return;
            }

            RentalReceipt? receipt;
            using (var form = _forms.Create<KiralamaForm>(_session, entry.Item.Film.Id))
            {
                receipt = form.ShowDialog(this) == DialogResult.OK ? form.Receipt : null;
            }

            if (receipt is not null && !IsDisposed)
            {
                await this.RunAsync(LoadAsync);
                ShowStatus(StatusKind.Success, KiralamaForm.Confirmation(entry.Item.Film.Title, receipt));
            }
        }

        private void UpdateButtons()
        {
            var selected = Selected;
            btnKirala.Enabled = selected is { Item.IsAvailable: true };
            btnCikar.Enabled = selected is not null;
        }

        private void BuildLayout()
        {
            Name = nameof(IzlemeListesiForm);
            var p = Theme.Current;
            lblOzet.Font = Theme.Fonts.BodyStrong;
            lblOzet.Margin = new Padding(0, 0, 0, Spacing.M);
            dgvListe.EmptyText = "Listeniz boş. Katalogda beğendiğiniz filmin yıldızlı liste düğmesine basın.";
            dgvListe.ColumnWeights = new Dictionary<string, float>
            {
                [nameof(EntryRow.Title)] = 200,
                [nameof(EntryRow.Year)] = 60,
                [nameof(EntryRow.Price)] = 120,
                [nameof(EntryRow.State)] = 110,
            };
            dgvListe.CellTone = (column, value) => column == nameof(EntryRow.State) ? StateColor(p, value as string) : null;
            var card = FormLayout.Card("listCard", null, dgvListe);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("listPage", (lblOzet, false), (card, true)));

            AcceptButton = btnKirala;
            SetFooterNote("Kiraladığınız film listenizden kendiliğinden çıkar; listede en çok 100 film olabilir.");
            SetFooterButtons(btnKirala, btnCikar, CreateCloseButton("btnKapat"));
            FinishLayout();
        }

        /// <summary>A film that can be rented reads in the success color, one that cannot in the warning color.</summary>
        internal static Color? StateColor(ThemePalette palette, string? state) => state switch
        {
            "Müsait" => palette.Success,
            "Kirada" or "Kiralamaya kapalı" or "Kaldırıldı" => palette.Warning,
            _ => null,
        };

        /// <summary>
        /// A row of the list; film texts are plain text in cells. A film unlisted since it was added shows
        /// its title and "Kaldırıldı" only.
        /// </summary>
        private sealed class EntryRow(WatchlistView entry)
        {
            [Browsable(false)]
            public WatchlistView Entry { get; } = entry;

            [DisplayName("Film")]
            public string Title => Entry.Item.Film.Title;

            [DisplayName("Tür")]
            public string Genre => Listed ? Entry.Item.Film.Genre : string.Empty;

            [DisplayName("Yıl")]
            public string Year => Listed ? Entry.Item.Film.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;

            [DisplayName("Puan")]
            public string Rating => Listed ? FilmText.Rating(Entry.Item.AverageRating, Entry.Item.RatingCount) : string.Empty;

            [DisplayName("Günlük Ücret")]
            public string Price => Listed ? DisplayFormat.Money(Entry.Item.Film.DailyPrice) : string.Empty;

            [DisplayName("Durum")]
            public string State => Listed ? FilmRow.Describe(Entry.Item.Film) : "Kaldırıldı";

            [DisplayName("Eklenme")]
            public string Added => DisplayFormat.Date(Entry.AddedOn);

            private bool Listed => Entry.Item.Film.IsListed;
        }
    }
}
