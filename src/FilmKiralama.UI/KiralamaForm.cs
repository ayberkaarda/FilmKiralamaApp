using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Rental of one film: its poster, details, average rating and visible reviews, then the number of days,
    /// the total and the confirmation. Closes with <see cref="DialogResult.OK"/> and <see cref="Receipt"/> set
    /// when the rental was stored.
    /// </summary>
    public sealed class KiralamaForm : AppDialog
    {
        private readonly IFilmService _films;
        private readonly IRentalService _rentals;
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly int _filmId;
        private readonly ExclusiveOperation _rent = new();
        private int _days = 1;

        private readonly PosterBox picAfis = new() { Name = nameof(picAfis) };
        private readonly HeadingLabel lblAd = new() { Name = nameof(lblAd) };
        private readonly Label lblBilgi = FormLayout.Text(nameof(lblBilgi));
        private readonly Label lblYonetmen = FormLayout.Text(nameof(lblYonetmen));
        private readonly TextBox txtHakkinda = new() { Name = nameof(txtHakkinda), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, TabStop = false };
        private readonly ThemedButton btnAzalt = FormLayout.Button(nameof(btnAzalt), "Bir gün azalt", ButtonKind.Secondary, Glyphs.Remove);
        private readonly TextBox txtGunSayisi = new() { Name = nameof(txtGunSayisi), Text = "1", TextAlign = HorizontalAlignment.Center, MaxLength = 4 };
        private readonly ThemedButton btnArtir = FormLayout.Button(nameof(btnArtir), "Bir gün artır", ButtonKind.Secondary, Glyphs.Add);
        private readonly Label lblUcret = FormLayout.Text(nameof(lblUcret));
        private readonly Label lblToplamTutar = FormLayout.Text(nameof(lblToplamTutar));
        private readonly Label lblPuan = FormLayout.Text(nameof(lblPuan));
        private readonly TextBox txtYorumlar = new() { Name = nameof(txtYorumlar), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, TabStop = false };
        private readonly ThemedButton btnOdemeyeGec = FormLayout.Button(nameof(btnOdemeyeGec), "Ödemeye geç", ButtonKind.Primary, Glyphs.Cart);

        public KiralamaForm(IFilmService films, IRentalService rentals, IReviewService reviews, UserSession session, int filmId)
            : base("Kiralama Ekranı", "Gün sayısını seçin, tutarı görün ve kiralamayı onaylayın.", Glyphs.Cart, new Size(1000, 700), new Size(820, 600), modal: true)
        {
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(session);

            _films = films;
            _rentals = rentals;
            _reviews = reviews;
            _session = session;
            _filmId = filmId;
            BuildLayout();
            _rent.HoldClosingOf(this);

            btnOdemeyeGec.Enabled = false;
            txtGunSayisi.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            };
            txtGunSayisi.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyTypedDays();
                }
            };
            txtGunSayisi.Leave += (_, _) => ApplyTypedDays();
            btnArtir.Click += (_, _) =>
            {
                ApplyTypedDays();
                SetDays(_days + 1);
            };
            btnAzalt.Click += (_, _) =>
            {
                ApplyTypedDays();
                SetDays(_days - 1);
            };
            btnOdemeyeGec.Click += async (_, _) => await RentAndCloseAsync();
        }

        /// <summary>Shows the poster; replaced by the screenshot tool so no download starts.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PosterLoader Posters { get; set; } = PosterLoader.Default;

        /// <summary>The film as it was loaded; its price is the one the customer confirms.</summary>
        internal Film? Film { get; private set; }

        internal int Days => _days;

        /// <summary>The stored rental, once the customer confirmed it.</summary>
        internal RentalReceipt? Receipt { get; private set; }

        /// <summary>The message the screen that opened the rental shows once it is stored.</summary>
        internal static string Confirmation(string title, RentalReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            return $"\"{title}\" kiralandı. İade tarihi: {DisplayFormat.Date(receipt.DueOn)}, toplam tutar: {DisplayFormat.Money(receipt.Total)}.";
        }

        internal async Task LoadFilmAsync()
        {
            var film = await _films.GetAsync(_session, _filmId);
            Film = film;
            lblAd.Text = film.Title;
            lblBilgi.Text = FilmText.YearAndGenre(film);
            lblYonetmen.Text = "Yönetmen: " + film.Director;
            txtHakkinda.Text = string.IsNullOrWhiteSpace(film.About) ? "Bu film için açıklama eklenmemiş." : film.About;
            lblUcret.Text = "Günlük ücret: " + DisplayFormat.Money(film.DailyPrice);
            picAfis.AccessibleName = film.Title + " afişi";
            Posters.Show(picAfis, film.ImageUrl);
            btnOdemeyeGec.Enabled = true;
            UpdateTotal();

            var reviews = await _reviews.GetFilmReviewsAsync(_session, _filmId);
            lblPuan.Text = "Puan: " + ReviewDisplay.Summary(reviews.Average, reviews.Count);
            txtYorumlar.Text = ReviewDisplay.Describe(reviews.Reviews);
        }

        /// <summary>
        /// Shows the price and dates, asks for confirmation and rents the film; ignored while a previous
        /// attempt is still running. Returns whether the rental was stored.
        /// </summary>
        internal async Task<bool> RentAsync()
        {
            if (Film is not { } film)
            {
                return false;
            }

            ApplyTypedDays();
            var rented = false;
            await this.RunExclusiveAsync(
                _rent,
                async () =>
                {
                    var quote = await _rentals.QuoteAsync(film.Id, film.DailyPrice, _days);
                    var question =
                        $"Toplam Tutar: {DisplayFormat.Money(quote.Total)}\n"
                        + $"Kiralama Başlangıç Tarihi: {DisplayFormat.Date(quote.StartsOn)}\n"
                        + $"Kiralama Bitiş Tarihi: {DisplayFormat.Date(quote.DueOn)}\n\n"
                        + "Kiralamayı onaylıyor musunuz?";
                    if (!Prompts.Confirm(question))
                    {
                        Prompts.Warn("Kiralama iptal edildi.");
                        return;
                    }

                    Receipt = await _rentals.RentAsync(_session, quote);
                    rented = true;
                },
                btnOdemeyeGec,
                btnArtir,
                btnAzalt);
            return rented;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadFilmAsync);
        }

        private async Task RentAndCloseAsync()
        {
            if (await RentAsync() && !IsDisposed)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void UpdateTotal()
        {
            if (Film is { } film)
            {
                lblToplamTutar.Text = "Toplam: " + DisplayFormat.Money(film.DailyPrice * _days);
            }
        }

        /// <summary>Takes the typed number of days, kept between 1 and the longest rental.</summary>
        private void ApplyTypedDays()
        {
            var max = _rentals.Policy.MaxRentalDays;
            _days = int.TryParse(txtGunSayisi.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var typed)
                ? Math.Clamp(typed, 1, max)
                : 1;
            txtGunSayisi.Text = _days.ToString(CultureInfo.InvariantCulture);
            UpdateTotal();
        }

        /// <summary>Sets the number of days as the plus and minus buttons do, kept between 1 and the longest rental.</summary>
        internal void ChooseDays(int days) => SetDays(days);

        private void SetDays(int days)
        {
            _days = Math.Clamp(days, 1, _rentals.Policy.MaxRentalDays);
            txtGunSayisi.Text = _days.ToString(CultureInfo.InvariantCulture);
            UpdateTotal();
        }

        private void BuildLayout()
        {
            Name = nameof(KiralamaForm);
            var p = Theme.Current;

            // The poster keeps its 2:3 shape inside a card of its own, at the top of its column.
            picAfis.Width = Sizes.PosterLargeWidth;
            var posterCard = FormLayout.Card("posterCard", null, picAfis, fill: false);
            posterCard.Padding = new Padding(Spacing.M);
            posterCard.Margin = Padding.Empty;
            var left = FormLayout.Page("leftColumn", (posterCard, false));

            lblAd.Font = Theme.Fonts.Display;
            lblAd.ForeColor = p.Text;
            lblAd.Dock = DockStyle.Top;
            lblAd.Margin = Padding.Empty;
            lblBilgi.ForeColor = p.TextSecondary;
            lblYonetmen.ForeColor = p.TextSecondary;
            lblYonetmen.Margin = new Padding(0, Spacing.XS / 2, 0, Spacing.M);
            var about = FormLayout.Input(txtHakkinda, Sizes.NoteHeight + Spacing.L);
            about.Margin = Padding.Empty;
            txtHakkinda.AccessibleName = "Hakkında";
            var details = FormLayout.Card("detailsCard", null, FormLayout.Stack("detailsStack", lblAd, lblBilgi, lblYonetmen, about), fill: false);

            // Days: minus, the number, plus; then the daily price and the total, read from a step away.
            txtGunSayisi.AccessibleName = "Gün sayısı";
            txtGunSayisi.Font = Theme.Fonts.BodyStrong;
            btnAzalt.IconOnly = true;
            btnArtir.IconOnly = true;
            var daysField = FormLayout.Input(txtGunSayisi);
            daysField.Width = 72;
            daysField.MinimumSize = new Size(72, daysField.MinimumSize.Height);
            var stepper = FormLayout.Bar("dayStepper", (btnAzalt, false), (daysField, false), (btnArtir, false));
            btnAzalt.Margin = new Padding(0, 0, Spacing.S, 0);
            btnArtir.Margin = new Padding(Spacing.S, 0, 0, 0);
            daysField.Margin = Padding.Empty;
            var daysCaption = FormLayout.Caption("Gün sayısı");
            var maxHint = FormLayout.Hint(string.Create(CultureInfo.CurrentCulture, $"1 ile {_rentals.Policy.MaxRentalDays} gün arası."));
            lblUcret.ForeColor = p.TextSecondary;
            lblUcret.Anchor = AnchorStyles.Right;
            lblToplamTutar.Font = Theme.Fonts.Figure;
            lblToplamTutar.ForeColor = p.AccentText;
            lblToplamTutar.Anchor = AnchorStyles.Right;
            var prices = FormLayout.Stack("priceStack", lblUcret, lblToplamTutar);
            lblUcret.Dock = lblToplamTutar.Dock = DockStyle.None;
            prices.Dock = DockStyle.None;
            prices.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var daysStack = FormLayout.Stack("daysStack", daysCaption, stepper, maxHint);
            stepper.Dock = DockStyle.None;
            stepper.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var rentRow = FormLayout.FieldRow("rentRow", (daysStack, 50), (prices, 50));
            var rentCard = FormLayout.Card("rentCard", "Kiralama", rentRow, fill: false);

            lblPuan.Font = Theme.Fonts.BodyStrong;
            lblPuan.ForeColor = p.WarmText;
            lblPuan.Margin = new Padding(0, 0, 0, Spacing.S);
            txtYorumlar.AccessibleName = "Yorumlar";
            var reviewsBox = FormLayout.Input(txtYorumlar, Sizes.NoteHeight);
            var reviews = FormLayout.Card("reviewsCard", "Yorumlar", FormLayout.Page("reviewsPage", (lblPuan, false), (reviewsBox, true)));
            reviews.Margin = Padding.Empty;

            var right = FormLayout.Page("rightColumn", (details, false), (rentCard, false), (reviews, true));
            Content.Controls.Add(FormLayout.Columns("layout", left, 30, right));

            AcceptButton = btnOdemeyeGec;
            SetFooterNote("Ödeme alınmaz; kiralama onaylandığında kopya sizin için ayrılır.");
            SetFooterButtons(btnOdemeyeGec, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
