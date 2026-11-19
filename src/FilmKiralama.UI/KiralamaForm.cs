using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Rental of one film: its average rating and visible reviews, then the number of days, the total and
    /// the confirmation. Closes with <see cref="DialogResult.OK"/> when the rental was stored.
    /// </summary>
    public partial class KiralamaForm : Form
    {
        private readonly IFilmService _films;
        private readonly IRentalService _rentals;
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly int _filmId;
        private readonly ExclusiveOperation _rent = new();

        // The film's average rating, and its visible reviews as plain text.
        private readonly Label _rating;
        private readonly TextBox _reviewText;
        private int _days = 1;

        public KiralamaForm(IFilmService films, IRentalService rentals, IReviewService reviews, UserSession session, int filmId)
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
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            AutoScaleMode = AutoScaleMode.None;
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            Font = new Font("Segoe UI", 12);
            btnArtir.ForeColor = Color.Black;
            btnAzalt.ForeColor = Color.Black;
            btnOdemeyeGec.ForeColor = Color.Black;
            txtGunSayisi.TextAlign = HorizontalAlignment.Center;
            txtGunSayisi.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            txtGunSayisi.Text = "1";

            lblHakkinda.AutoSize = false;
            lblHakkinda.Width = 370;
            lblHakkinda.MaximumSize = new Size(370, 0);
            lblHakkinda.AutoSize = true;
            lblHakkinda.TextAlign = ContentAlignment.TopLeft;
            pictureBox1.BorderStyle = BorderStyle.None;
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            btnOdemeyeGec.Enabled = false;

            // Reviews are user text: a label without mnemonics and a read-only text box show it as typed.
            _rating = new Label
            {
                Name = "lblPuan",
                Location = new Point(840, 130),
                Size = new Size(280, 28),
                UseMnemonic = false,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
            };
            _reviewText = new TextBox
            {
                Name = "txtYorumlar",
                Location = new Point(840, 162),
                Size = new Size(280, 355),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10),
                TabStop = false,
            };
            Controls.AddRange([_rating, _reviewText]);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The film as it was loaded; its price is the one the customer confirms.</summary>
        internal Film? Film { get; private set; }

        internal int Days => _days;

        internal async Task LoadFilmAsync()
        {
            var film = await _films.GetAsync(_session, _filmId);
            Film = film;
            lblMarka.Text = film.Title;
            lblModel.Text = film.Genre;
            lblYil.Text = film.Year.ToString(CultureInfo.InvariantCulture);
            lblVites.Text = film.Director;
            lblUcret.Text = DisplayFormat.Money(film.DailyPrice);
            lblHakkinda.Text = film.About ?? string.Empty;
            pictureBox1.ShowPoster(film.ImageUrl);
            btnOdemeyeGec.Enabled = true;
            UpdateTotal();

            var reviews = await _reviews.GetFilmReviewsAsync(_session, _filmId);
            _rating.Text = "Puan: " + ReviewDisplay.Summary(reviews.Average, reviews.Count);
            _reviewText.Text = ReviewDisplay.Describe(reviews.Reviews);
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

                    await _rentals.RentAsync(_session, quote);
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

        private void UpdateTotal()
        {
            if (Film is { } film)
            {
                lblToplamTutar.Text = "Toplam Tutar: " + DisplayFormat.Money(film.DailyPrice * _days);
                lblToplamTutar.Show();
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

        private void SetDays(int days)
        {
            _days = Math.Clamp(days, 1, _rentals.Policy.MaxRentalDays);
            txtGunSayisi.Text = _days.ToString(CultureInfo.InvariantCulture);
            UpdateTotal();
        }

        private void btnArtir_Click_1(object? sender, EventArgs e)
        {
            ApplyTypedDays();
            SetDays(_days + 1);
        }

        private void btnAzalt_Click_1(object? sender, EventArgs e)
        {
            ApplyTypedDays();
            SetDays(_days - 1);
        }

        private void txtGunSayisi_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void txtGunSayisi_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyTypedDays();
            }
        }

        private void txtGunSayisi_Leave(object? sender, EventArgs e) => ApplyTypedDays();

        private void txtGunSayisi_Validating(object? sender, System.ComponentModel.CancelEventArgs e) => ApplyTypedDays();

        private async void btnOdemeyeGec_Click_1(object? sender, EventArgs e)
        {
            if (await RentAsync())
            {
                Prompts.Inform("Kiralama işlemi onaylandı ve kaydedildi. Anasayfaya yönlendiriliyorsunuz.");
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
