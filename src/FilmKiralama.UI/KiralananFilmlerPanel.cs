using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>The admin's list of every active rental, filtered by genre, year and director, with the return of each.</summary>
    public partial class KiralananFilmlerPanel : Form
    {
        private readonly IRentalService _rentals;
        private readonly IFilmService _films;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _return = new();
        private readonly RequestSequence _loads = new();
        private readonly Panel _filterPanel;
        private readonly FilmFilterBoxes _filter;
        private readonly Panel _listPanel;
        private FilmFilter _applied = FilmFilter.None;

        public KiralananFilmlerPanel(IRentalService rentals, IFilmService films, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _films = films;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            _listPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(_listPanel);
            (_filterPanel, _filter) = BuildFilterPanel();
            // The filling list docks last, below the filter bar.
            Controls.SetChildIndex(_listPanel, 0);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
            var rentals = await _rentals.GetActiveRentalsAsync(_session, _applied);
            if (_loads.IsLatest(request) && !IsDisposed)
            {
                ShowRentals(rentals);
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
                    var preview = _rentals.PreviewReturn(rental);
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
                [.. ReturnButtons]);
            return returned;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private (Panel Panel, FilmFilterBoxes Filter) BuildFilterPanel()
        {
            var panel = new Panel
            {
                Size = new Size(Width, 60),
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(245, 245, 245),
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(panel);

            var genre = new ComboBox { Name = "cbTur", Location = new Point(60, 15), Width = 120 };
            var year = new ComboBox { Name = "cbYil", Location = new Point(260, 15), Width = 120 };
            var director = new ComboBox { Name = "cbYonetmen", Location = new Point(490, 15), Width = 120 };
            var apply = new Button
            {
                Text = "Filtrele",
                Location = new Point(640, 12),
                Size = new Size(100, 30),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            apply.Click += async (_, _) => await this.RunAsync(ApplyFilterAsync);

            panel.Controls.AddRange(
            [
                new Label { Text = "Tür:", Location = new Point(10, 20), Size = new Size(50, 15), ForeColor = Color.Black },
                genre,
                new Label { Text = "Yıl:", Location = new Point(200, 20), Size = new Size(58, 15), ForeColor = Color.Black },
                year,
                new Label { Text = "Yönetmen:", Location = new Point(410, 20), Size = new Size(80, 15), ForeColor = Color.Black },
                director,
                apply,
            ]);
            return (panel, new FilmFilterBoxes(genre, director, year));
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var buttons = new List<Button>();
            _listPanel.SuspendLayout();
            try
            {
                foreach (Control old in _listPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var top = 20 + _listPanel.AutoScrollPosition.Y;
                for (var i = 0; i < rentals.Count; i++)
                {
                    var rental = rentals[i];
                    var card = new Panel
                    {
                        Size = new Size(600, 220),
                        Location = new Point(i % 2 == 0 ? 20 : 650, top + (i / 2 * 240)),
                        BackColor = Color.FromArgb(230, 230, 230),
                    };

                    var poster = new PictureBox { Size = new Size(150, 220), Location = new Point(0, 0), SizeMode = PictureBoxSizeMode.StretchImage };
                    poster.ShowPoster(rental.ImageUrl);
                    card.Controls.Add(poster);

                    card.Controls.Add(new Label
                    {
                        Location = new Point(180, 20),
                        Size = new Size(235, 190),
                        Font = new Font("Segoe UI", 10, FontStyle.Bold),
                        ForeColor = Color.Black,
                        Text = $"Ad: {rental.FilmTitle}\n\n"
                            + $"Alış Tarihi: {DisplayFormat.Date(rental.RentedOn)}\n\n"
                            + $"İade Tarihi: {DisplayFormat.Date(rental.DueOn)}\n\n"
                            + $"Toplam Ücret: {DisplayFormat.Money(rental.Price)}\n\n"
                            + $"Kiracı: {rental.UserName} (ID: {rental.UserId})",
                    });

                    var returnButton = new Button
                    {
                        Text = "İade Al",
                        Size = new Size(150, 40),
                        Location = new Point(420, 150),
                        BackColor = Color.IndianRed,
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat,
                    };
                    var current = rental;
                    returnButton.Click += async (_, _) => await ReturnAsync(current);
                    card.Controls.Add(returnButton);
                    buttons.Add(returnButton);
                    _listPanel.Controls.Add(card);
                }
            }
            finally
            {
                _listPanel.ResumeLayout();
            }

            ReturnButtons = buttons;
        }
    }
}
