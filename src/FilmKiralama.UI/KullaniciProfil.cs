using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's profile: own active rentals as cards with their return, the returned films the customer
    /// can rate, and the password change.
    /// </summary>
    public sealed class KullaniciProfil : AppDialog
    {
        private readonly IRentalService _rentals;
        private readonly IReviewService _reviews;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _return = new();
        private readonly RequestSequence _loads = new();
        private readonly RequestSequence _reviewableLoads = new();
        private bool _binding;

        private readonly CardGrid pnlKiralamalar = new() { Name = nameof(pnlKiralamalar), ItemWidth = 320 };
        private readonly ThemedListBox lstPuanlanabilir = new() { Name = nameof(lstPuanlanabilir) };
        private readonly ThemedButton btnPuanVer = FormLayout.Button(nameof(btnPuanVer), "Puan ver / düzenle", ButtonKind.Primary, Glyphs.Star);
        private readonly ThemedButton btnSifreDegistir = FormLayout.Button(nameof(btnSifreDegistir), "Şifre değiştir", ButtonKind.Secondary, Glyphs.Key);

        public KullaniciProfil(IRentalService rentals, IReviewService reviews, IFormFactory forms, UserSession session)
            : base("Kullanıcı Profili", "Hoş geldin, " + AppText.DisplayName(session?.UserName ?? string.Empty) + ". Kiraladıklarınız ve puanlarınız burada.", Glyphs.Person, new Size(1100, 680), new Size(860, 540), modal: true)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _reviews = reviews;
            _forms = forms;
            _session = session;
            BuildLayout();
            _return.HoldClosingOf(this);

            btnPuanVer.Enabled = false;
            lstPuanlanabilir.SelectedIndexChanged += (_, _) =>
            {
                // Filling the list is not a choice of the user.
                if (!_binding)
                {
                    btnPuanVer.Enabled = lstPuanlanabilir.SelectedItem is ReviewableItem;
                }
            };
            lstPuanlanabilir.DoubleClick += async (_, _) => await RateSelectedAsync();
            btnPuanVer.Click += async (_, _) => await RateSelectedAsync();
            btnSifreDegistir.Click += (_, _) => ChangePassword();
        }

        /// <summary>Shows the posters of the rentals; replaced by the screenshot tool so no download starts.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PosterLoader Posters { get; set; } = PosterLoader.Default;

        /// <summary>The rentals shown now, for tests.</summary>
        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        /// <summary>The return buttons shown now, for tests.</summary>
        internal IReadOnlyList<Button> ReturnButtons { get; private set; } = [];

        /// <summary>The returned films the customer can rate, as shown now.</summary>
        internal IReadOnlyList<ReviewableFilm> ShownReviewable { get; private set; } = [];

        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            pnlKiralamalar.IsLoading = true;
            try
            {
                var rentals = await _rentals.GetMyActiveRentalsAsync(_session);
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

        /// <summary>Loads the films the customer may rate; only the latest request is shown and nothing is selected.</summary>
        internal async Task LoadReviewableAsync()
        {
            var request = _reviewableLoads.Next();
            lstPuanlanabilir.IsLoading = true;
            try
            {
                var films = await _reviews.GetReviewableAsync(_session);
                if (!_reviewableLoads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownReviewable = films;
                _binding = true;
                try
                {
                    lstPuanlanabilir.ReplaceItems(films.Select(f => new ReviewableItem(f)));
                }
                finally
                {
                    _binding = false;
                }

                btnPuanVer.Enabled = false;
            }
            finally
            {
                if (_reviewableLoads.IsLatest(request) && !IsDisposed)
                {
                    lstPuanlanabilir.IsLoading = false;
                }
            }
        }

        /// <summary>
        /// Returns one rental after showing its late fee; ignored while another return is still running, and
        /// every return button stays disabled meanwhile. Returns whether it was returned.
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
                        ? $"\"{rental.FilmTitle}\" {preview.OverdueDays} gün gecikmeyle iade edilecek. Gecikme ücreti: {DisplayFormat.Money(preview.LateFee)}\n\nİade etmek istiyor musunuz?"
                        : $"\"{rental.FilmTitle}\" iade edilecek.\n\nİade etmek istiyor musunuz?";
                    if (!Prompts.Confirm(question))
                    {
                        return;
                    }

                    var receipt = await _rentals.ReturnAsync(_session, rental.Id);
                    returned = true;
                    Prompts.Inform(receipt.LateFee > 0m
                        ? $"Film iade edildi. Gecikme ücreti: {DisplayFormat.Money(receipt.LateFee)}"
                        : "Film iade edildi. Artık puan verebilirsiniz.");
                    await LoadRentalsAsync();

                    // A returned film can be rated now.
                    await LoadReviewableAsync();
                },
                [.. ReturnButtons]);
            return returned;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(async () =>
            {
                await LoadRentalsAsync();
                await LoadReviewableAsync();
            });
        }

        private async Task RateSelectedAsync()
        {
            if (lstPuanlanabilir.SelectedItem is not ReviewableItem item || _return.IsRunning)
            {
                return;
            }

            string? message;
            using (var form = _forms.Create<DegerlendirmeForm>(_session, item.Film))
            {
                message = form.ShowDialog(this) == DialogResult.OK ? form.ResultMessage : null;
            }

            if (message is not null && !IsDisposed)
            {
                ShowStatus(StatusKind.Success, message);
                await this.RunAsync(LoadReviewableAsync);
            }
        }

        private void ChangePassword()
        {
            using var form = _forms.Create<SifreDegistirmeForm>(_session);
            if (form.ShowDialog(this) == DialogResult.OK && !IsDisposed)
            {
                ShowStatus(StatusKind.Success, "Şifreniz değiştirildi.");
            }
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var cards = new List<RentalCard>();
            foreach (var rental in rentals)
            {
                var card = new RentalCard(rental, "İade et");
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
            Name = nameof(KullaniciProfil);
            pnlKiralamalar.EmptyText = "Şu anda kiradaki filminiz yok. Katalogdan bir film kiraladığınızda burada görünür.";
            pnlKiralamalar.BackColor = Theme.Current.Surface;
            pnlKiralamalar.MinimumSize = new Size(0, 200);
            var rentalsCard = FormLayout.Card("rentalsCard", "Kiradaki filmlerim", pnlKiralamalar);
            rentalsCard.Margin = Padding.Empty;

            lstPuanlanabilir.Describe = item => item is ReviewableItem reviewable ? reviewable.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstPuanlanabilir.EmptyText = "Puan verebileceğiniz film yok. Kiraladığınız filmleri iade ettikten sonra puanlayabilirsiniz.";
            lstPuanlanabilir.AccessibleName = "Puan verebileceğiniz filmler";
            btnPuanVer.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnPuanVer.Margin = new Padding(0, Spacing.M, 0, 0);
            var hint = FormLayout.Hint("Kiralayıp iade ettiğiniz filmler. Bir filmi seçin ya da çift tıklayın.");
            hint.Margin = new Padding(0, 0, 0, Spacing.S);
            var rateCard = FormLayout.Card("rateCard", "Puan verebileceğiniz filmler", FormLayout.Page("ratePage", (hint, false), (lstPuanlanabilir, true), (btnPuanVer, false)));
            rateCard.Margin = Padding.Empty;
            btnPuanVer.Dock = DockStyle.None;

            Content.Controls.Add(FormLayout.Columns("layout", rentalsCard, 58, rateCard));
            SetFooterNote("İade edilen filmin kopyası rafa döner; geciken iadelerde gecikme ücreti önce gösterilir.");
            SetFooterButtons(btnSifreDegistir, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
