using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's profile: own active rentals with their return, the returned films the customer can
    /// rate, and the password change.
    /// </summary>
    public partial class KullaniciProfil : Form
    {
        private readonly IRentalService _rentals;
        private readonly IReviewService _reviews;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _return = new();
        private readonly RequestSequence _loads = new();
        private readonly RequestSequence _reviewableLoads = new();
        private readonly Panel _scrollPanel;
        private readonly ListBox _reviewable;
        private readonly Button _rate;

        public KullaniciProfil(IRentalService rentals, IReviewService reviews, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _reviews = reviews;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 190);
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            BuildHeader();
            (_reviewable, _rate) = BuildReviewPanel();
            _scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackgroundImage = AppAssets.Background,
                Padding = new Padding(0, 10, 0, 0),
            };
            Controls.Add(_scrollPanel);
            Controls.SetChildIndex(_scrollPanel, 0);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The rentals shown now, for tests.</summary>
        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        /// <summary>The return buttons shown now, for tests.</summary>
        internal IReadOnlyList<Button> ReturnButtons { get; private set; } = [];

        /// <summary>The returned films the customer can rate, as shown now.</summary>
        internal IReadOnlyList<ReviewableFilm> ShownReviewable { get; private set; } = [];

        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            var rentals = await _rentals.GetMyActiveRentalsAsync(_session);
            if (_loads.IsLatest(request) && !IsDisposed)
            {
                ShowRentals(rentals);
            }
        }

        /// <summary>Loads the films the customer may rate; only the latest request is shown and nothing is selected.</summary>
        internal async Task LoadReviewableAsync()
        {
            var request = _reviewableLoads.Next();
            var films = await _reviews.GetReviewableAsync(_session);
            if (!_reviewableLoads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownReviewable = films;
            _reviewable.BeginUpdate();
            try
            {
                _reviewable.Items.Clear();
                foreach (var film in films)
                {
                    _reviewable.Items.Add(new ReviewableItem(film));
                }

                _reviewable.SelectedIndex = -1;
            }
            finally
            {
                _reviewable.EndUpdate();
            }

            _rate.Enabled = false;
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
                    var preview = _rentals.PreviewReturn(rental);
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
                        : "Film iade edildi.");
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

        private (ListBox List, Button Rate) BuildReviewPanel()
        {
            var panel = new Panel { Name = "pnlDegerlendir", Dock = DockStyle.Bottom, Height = 190, BackColor = Color.FromArgb(240, 240, 240) };
            var caption = new Label
            {
                Text = "Puan verebileceğiniz filmler (kiralayıp iade ettikleriniz)",
                Location = new Point(20, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.Black,
            };
            var list = new ListBox
            {
                Name = "lstPuanlanabilir",
                Location = new Point(20, 36),
                Size = new Size(600, 140),
                Font = new Font("Segoe UI", 10),
                IntegralHeight = false,
            };
            var rate = new Button
            {
                Name = "btnPuanVer",
                Text = "Puan Ver / Düzenle",
                Location = new Point(640, 36),
                Size = new Size(180, 40),
                Enabled = false,
            };
            list.SelectedIndexChanged += (_, _) => rate.Enabled = list.SelectedItem is ReviewableItem;
            list.DoubleClick += async (_, _) => await RateSelectedAsync();
            rate.Click += async (_, _) => await RateSelectedAsync();
            panel.Controls.AddRange([caption, list, rate]);
            Controls.Add(panel);
            return (list, rate);
        }

        private async Task RateSelectedAsync()
        {
            if (_reviewable.SelectedItem is not ReviewableItem item)
            {
                return;
            }

            DialogResult result;
            using (var form = _forms.Create<DegerlendirmeForm>(_session, item.Film))
            {
                result = form.ShowDialog(this);
            }

            if (result == DialogResult.OK)
            {
                await this.RunAsync(LoadReviewableAsync);
            }
        }

        private Panel BuildHeader()
        {
            var header = new Panel
            {
                Size = new Size(ClientSize.Width, 80),
                Dock = DockStyle.Top,
                BackColor = Color.SteelBlue,
            };
            Controls.Add(header);

            header.Controls.Add(new Label
            {
                Text = "Hoş geldin, " + AppText.DisplayName(_session.UserName),
                Font = new Font("Arial", 14, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 25),
                AutoSize = true,
            });

            var changePassword = new Button
            {
                Text = "Şifre Değiştir",
                Size = new Size(120, 35),
                Location = new Point(header.Width - 140, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.White,
                ForeColor = Color.SteelBlue,
                FlatStyle = FlatStyle.Flat,
            };
            changePassword.Click += (_, _) =>
            {
                using var form = _forms.Create<SifreDegistirmeForm>(_session);
                form.ShowDialog(this);
            };
            header.Controls.Add(changePassword);
            return header;
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var buttons = new List<Button>();
            _scrollPanel.SuspendLayout();
            try
            {
                foreach (Control old in _scrollPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var y = 20 + _scrollPanel.AutoScrollPosition.Y;
                foreach (var rental in rentals)
                {
                    var card = new Panel
                    {
                        Size = new Size(600, 210),
                        Location = new Point(20, y),
                        BackColor = Color.FromArgb(230, 230, 230),
                    };

                    var poster = new PictureBox { Size = new Size(150, 210), Location = new Point(0, 0), SizeMode = PictureBoxSizeMode.StretchImage };
                    poster.ShowPoster(rental.ImageUrl);
                    card.Controls.Add(poster);

                    card.Controls.Add(new Label
                    {
                        Location = new Point(180, 20),
                        Size = new Size(230, 160),
                        Font = new Font("Segoe UI", 10, FontStyle.Bold),
                        ForeColor = Color.Black,
                        Text = $"Film: {rental.FilmTitle} - {rental.Genre}\n\n"
                            + $"Alış Tarihi: {DisplayFormat.Date(rental.RentedOn)}\n\n"
                            + $"İade Tarihi: {DisplayFormat.Date(rental.DueOn)}\n\n"
                            + $"Toplam Ücret: {DisplayFormat.Money(rental.Price)}",
                    });

                    var returnButton = new Button
                    {
                        Text = "İade Et",
                        Size = new Size(150, 40),
                        Location = new Point(420, 140),
                        BackColor = Color.IndianRed,
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat,
                    };
                    var current = rental;
                    returnButton.Click += async (_, _) => await ReturnAsync(current);
                    card.Controls.Add(returnButton);
                    buttons.Add(returnButton);

                    _scrollPanel.Controls.Add(card);
                    y = card.Bottom + 20;
                }
            }
            finally
            {
                _scrollPanel.ResumeLayout();
            }

            ReturnButtons = buttons;
        }
    }
}
