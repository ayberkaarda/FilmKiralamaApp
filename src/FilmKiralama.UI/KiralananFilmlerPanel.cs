using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>The admin's list of every active rental as cards, filtered by genre, year and director, with the return of each.</summary>
    public sealed class KiralananFilmlerPanel : AppDialog
    {
        private readonly IRentalService _rentals;
        private readonly IFilmService _films;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _return = new();
        private readonly RequestSequence _loads = new();
        private readonly FilmFilterBoxes _filter;
        private FilmFilter _applied = FilmFilter.None;

        private readonly ThemedComboBox cbTur = FormLayout.Picker(nameof(cbTur), 150);
        private readonly ThemedComboBox cbYil = FormLayout.Picker(nameof(cbYil), 100);
        private readonly ThemedComboBox cbYonetmen = FormLayout.Picker(nameof(cbYonetmen), 170);
        private readonly ThemedButton btnFiltrele = FormLayout.Button(nameof(btnFiltrele), "Filtrele", ButtonKind.Primary, Glyphs.Filter);
        private readonly CardGrid pnlKiralamalar = new() { Name = nameof(pnlKiralamalar), ItemWidth = 360 };

        public KiralananFilmlerPanel(IRentalService rentals, IFilmService films, UserSession session)
            : base("Kiralanan Filmler", "Kiradaki bütün filmler; geri gelen kopyayı buradan iade alın.", Glyphs.List, new Size(1120, 700), new Size(860, 540), modal: true)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _films = films;
            _session = session;
            _filter = new FilmFilterBoxes(cbTur, cbYonetmen, cbYil);
            BuildLayout();
            _return.HoldClosingOf(this);
            btnFiltrele.Click += async (_, _) => await this.RunAsync(ApplyFilterAsync);
        }

        /// <summary>Shows the posters of the rentals; replaced by the screenshot tool so no download starts.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PosterLoader Posters { get; set; } = PosterLoader.Default;

        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        internal IReadOnlyList<Button> ReturnButtons { get; private set; } = [];

        internal FilmFilterBoxes Filter => _filter;

        internal async Task RefreshAsync()
        {
            _filter.Fill(await _films.GetFilterOptionsAsync(_session));
            await LoadRentalsAsync();
        }

        /// <summary>Loads the rentals of the applied filter; only the latest request is shown.</summary>
        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            pnlKiralamalar.IsLoading = true;
            try
            {
                var rentals = await _rentals.GetActiveRentalsAsync(_session, _applied);
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    ShowRentals(rentals);
                }
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    pnlKiralamalar.IsLoading = false;
                }
            }
        }

        /// <summary>Applies the chosen filter, as the original "Filtrele" button did.</summary>
        internal Task ApplyFilterAsync()
        {
            _applied = _filter.Current;
            return LoadRentalsAsync();
        }

        /// <summary>
        /// Takes back one rental after showing its late fee; ignored while another return is running.
        /// Returns whether it was returned.
        /// </summary>
        internal async Task<bool> ReturnAsync(RentalDetails rental)
        {
            ArgumentNullException.ThrowIfNull(rental);

            var returned = false;
            await this.RunExclusiveAsync(
                _return,
                async () =>
                {
                    var preview = await _rentals.PreviewReturnAsync(rental);
                    var question = preview.LateFee > 0m
                        ? $"\"{rental.FilmTitle}\" {preview.OverdueDays} gün gecikmeli. Gecikme ücreti: {DisplayFormat.Money(preview.LateFee)}\n\nİade alınsın mı?"
                        : $"\"{rental.FilmTitle}\" iade alınsın mı?";
                    if (!Prompts.Confirm(question))
                    {
                        return;
                    }

                    var receipt = await _rentals.ReturnAsync(_session, rental.Id);
                    returned = true;
                    Prompts.Inform(receipt.LateFee > 0m
                        ? $"Kiralama iade alındı. Gecikme ücreti: {DisplayFormat.Money(receipt.LateFee)}"
                        : "Kiralama iade alındı.");
                    await LoadRentalsAsync();
                },
                [.. ReturnButtons, btnFiltrele]);
            return returned;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var cards = new List<RentalCard>();
            foreach (var rental in rentals)
            {
                var card = new RentalCard(rental, "İade al", showTenant: true);
                var current = rental;
                card.Action.Click += async (_, _) => await ReturnAsync(current);
                Posters.Show(card.Poster, rental.ImageUrl);
                cards.Add(card);
            }

            pnlKiralamalar.SetItems(cards);
            ReturnButtons = [.. cards.Select(c => c.Action)];
        }

        private void BuildLayout()
        {
            Name = nameof(KiralananFilmlerPanel);
            var filters = FormLayout.FieldRow(
                "filterRow",
                (FormLayout.Field("&Tür", cbTur), 30),
                (FormLayout.Field("&Yıl", cbYil), 20),
                (FormLayout.Field("Yö&netmen", cbYonetmen), 34),
                (FormLayout.FieldAction(btnFiltrele), 0));
            var filterCard = FormLayout.Card("filterCard", null, filters, fill: false);
            filterCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, Spacing.XS);
            filterCard.Margin = new Padding(0, 0, 0, Spacing.L);

            pnlKiralamalar.EmptyText = "Seçilen filtreye uyan, kirada bir film yok.";
            pnlKiralamalar.MinimumSize = new Size(0, 220);
            Content.Controls.Add(FormLayout.Page("rentalsPage", (filterCard, false), (pnlKiralamalar, true)));

            AcceptButton = btnFiltrele;
            SetFooterNote("İade alınan kopya rafa döner; geciken kiralamada gecikme ücreti önce gösterilir.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
