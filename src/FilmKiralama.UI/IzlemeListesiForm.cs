using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Watchlists;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>The customer's watchlist: which films are available now, rent one or take it off the list.</summary>
    public sealed class IzlemeListesiForm : Form
    {
        private readonly IWatchlistService _watchlist;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly DataGridView _grid;
        private readonly Label _summary;
        private readonly Button _rent;
        private readonly Button _remove;
        private bool _binding;

        public IzlemeListesiForm(IWatchlistService watchlist, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(watchlist);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _watchlist = watchlist;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            Text = "İzleme Listem";
            Icon = AppAssets.Icon;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(900, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _summary = new Label { Name = "lblOzet", Location = new Point(16, 16), Size = new Size(860, 26), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            _grid = new DataGridView
            {
                Name = "dgvListe",
                Location = new Point(16, 50),
                Size = new Size(868, 440),
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

            _rent = new Button { Name = "btnKirala", Text = "Kirala", Location = new Point(574, 506), Size = new Size(150, 36), Enabled = false };
            _remove = new Button { Name = "btnCikar", Text = "Listeden Çıkar", Location = new Point(734, 506), Size = new Size(150, 36), Enabled = false };
            _rent.Click += async (_, _) => await RentSelectedAsync();
            _remove.Click += async (_, _) => await RemoveAsync();
            Controls.AddRange([_summary, _grid, _rent, _remove]);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The list as shown now.</summary>
        internal IReadOnlyList<WatchlistView> ShownEntries { get; private set; } = [];

        /// <summary>The entry whose row is selected, or <see langword="null"/>.</summary>
        internal WatchlistView? Selected => _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].DataBoundItem is EntryRow row ? row.Entry : null;

        /// <summary>Loads the list; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync()
        {
            var request = _loads.Next();
            var entries = await _watchlist.GetMineAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownEntries = entries;
            _binding = true;
            try
            {
                _grid.DataSource = entries.Select(e => new EntryRow(e)).ToList();
                _grid.ClearSelection();
            }
            finally
            {
                _binding = false;
            }

            var available = entries.Count(e => e.Item.IsAvailable);
            _summary.Text = entries.Count == 0
                ? "İzleme listeniz boş. Katalogdaki filmleri \"Listeye Ekle\" ile ekleyebilirsiniz."
                : string.Create(CultureInfo.InvariantCulture, $"Listenizde {entries.Count} film var; {available} tanesi şu anda kiralanabilir.");
            UpdateButtons();
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
                },
                _rent,
                _remove);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadAsync);
        }

        /// <summary>Opens the rental of the selected film when it can be rented now; a rental takes it off the list.</summary>
        private async Task RentSelectedAsync()
        {
            if (Selected is not { Item.IsAvailable: true } entry)
            {
                return;
            }

            bool rented;
            using (var form = _forms.Create<KiralamaForm>(_session, entry.Item.Film.Id))
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
            var selected = Selected;
            _rent.Enabled = selected is { Item.IsAvailable: true };
            _remove.Enabled = selected is not null;
        }

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
            public string Rating => Listed ? ReviewDisplay.Summary(Entry.Item.AverageRating, Entry.Item.RatingCount) : string.Empty;

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
