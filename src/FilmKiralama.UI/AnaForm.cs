using System.Drawing.Drawing2D;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>The customer's film list: filter by genre, director and year, rent a film, open the profile.</summary>
    public partial class AnaForm : Form, ISignOutPanel
    {
        private const int CardWidth = 240;
        private const int CardHeight = 480;
        private const int CardSpacing = 40;
        private const int RowSpacing = 30;
        private const int CardsPerRow = 4;

        private readonly IFilmService _films;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly FilmFilterBoxes _filter;
        private readonly Panel _scrollPanel;

        public AnaForm(IFilmService films, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _films = films;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            var header = BuildHeader();
            var (filterPanel, filter) = BuildFilterPanel(header.Bottom + 10);
            _filter = filter;
            _filter.Changed += async (_, _) => await this.RunAsync(LoadFilmsAsync);

            _scrollPanel = new Panel
            {
                BackgroundImage = AppAssets.Background,
                BackgroundImageLayout = ImageLayout.Stretch,
                Location = new Point(0, filterPanel.Bottom + 10),
                AutoScroll = true,
            };
            _scrollPanel.Size = new Size(ClientSize.Width, ClientSize.Height - _scrollPanel.Top);
            Controls.Add(_scrollPanel);
        }

        public bool SignedOut { get; private set; }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The film cards shown now, for tests.</summary>
        internal IReadOnlyList<Film> ShownFilms { get; private set; } = [];

        internal FilmFilterBoxes Filter => _filter;

        /// <summary>Loads the filter values and the film list.</summary>
        internal async Task RefreshAsync()
        {
            _filter.Fill(await _films.GetFilterOptionsAsync(_session));
            await LoadFilmsAsync();
        }

        /// <summary>Loads the films of the current filter; only the latest request is shown.</summary>
        internal async Task LoadFilmsAsync()
        {
            var request = _loads.Next();
            var films = await _films.GetCatalogAsync(_session, _filter.Current);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShowFilms(films);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private Panel BuildHeader()
        {
            var headerColor = ColorTranslator.FromHtml("#2a2a3b");
            var header = new Panel { Size = new Size(ClientSize.Width, 50), BackColor = headerColor };
            Controls.Add(header);

            header.Controls.Add(new Label
            {
                Text = "Merhaba, " + AppText.DisplayName(_session.UserName),
                ForeColor = Color.White,
                BackColor = headerColor,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 15),
                Size = new Size(500, 30),
            });

            var profile = new Button
            {
                Text = "Profil",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 230, 10),
                BackColor = Color.LightSeaGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            profile.Click += async (_, _) =>
            {
                using (var form = _forms.Create<KullaniciProfil>(_session))
                {
                    form.ShowDialog(this);
                }

                // A return in the profile puts a copy back on the shelf.
                await this.RunAsync(RefreshAsync);
            };
            header.Controls.Add(profile);

            var signOut = new Button
            {
                Text = "Çıkış Yap",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 110, 10),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            signOut.Click += (_, _) =>
            {
                if (Prompts.Confirm("Çıkış yapmak istediğinizden emin misiniz?"))
                {
                    SignedOut = true;
                    Close();
                }
            };
            header.Controls.Add(signOut);
            return header;
        }

        private (Panel Panel, FilmFilterBoxes Filter) BuildFilterPanel(int top)
        {
            var panel = new Panel
            {
                Size = new Size(ClientSize.Width, 60),
                Location = new Point(0, top),
                BackgroundImage = AppAssets.Background,
            };
            Controls.Add(panel);

            Label Caption(string text, int x) => new()
            {
                Text = text,
                Location = new Point(x, 0),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
            };

            var genre = new ComboBox { Name = "turCombo", Location = new Point(20, 20), Width = 150 };
            var director = new ComboBox { Name = "yonetmenCombo", Location = new Point(190, 20), Width = 150 };
            var year = new ComboBox { Name = "yilCombo", Location = new Point(360, 20), Width = 150 };
            panel.Controls.AddRange([Caption("Tür", 20), Caption("Yönetmen", 190), Caption("Yıl", 360), genre, director, year]);
            return (panel, new FilmFilterBoxes(genre, director, year));
        }

        private void ShowFilms(IReadOnlyList<Film> films)
        {
            ShownFilms = films;
            _scrollPanel.SuspendLayout();
            try
            {
                foreach (Control old in _scrollPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var rowWidth = (CardsPerRow * (CardWidth + CardSpacing)) - CardSpacing;
                var left = (ClientSize.Width - rowWidth) / 2;
                for (var i = 0; i < films.Count; i++)
                {
                    var card = BuildCard(films[i]);
                    card.Location = new Point(
                        left + (i % CardsPerRow * (CardWidth + CardSpacing)),
                        10 + (i / CardsPerRow * (CardHeight + RowSpacing)) + _scrollPanel.AutoScrollPosition.Y);
                    _scrollPanel.Controls.Add(card);
                }
            }
            finally
            {
                _scrollPanel.ResumeLayout();
            }
        }

        private Panel BuildCard(Film film)
        {
            var card = new Panel
            {
                Size = new Size(CardWidth, CardHeight),
                BackColor = ColorTranslator.FromHtml("#2a2a3b"),
                Padding = new Padding(0),
            };
            card.Region = Rounded(card.Size, 20);

            var poster = new PictureBox
            {
                Size = new Size(240, 320),
                Location = new Point(0, 0),
                SizeMode = PictureBoxSizeMode.StretchImage,
            };
            poster.ShowPoster(film.ImageUrl);
            card.Controls.Add(poster);

            card.Controls.Add(new Label
            {
                Size = new Size(220, 85),
                Location = new Point(10, 325),
                Text = $"{film.Title}\nYönetmen: {film.Director}\n{film.Year} | {film.Genre} | {DisplayFormat.Money(film.DailyPrice)}",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
            });

            var rent = new Button
            {
                Text = "Kirala",
                Size = new Size(120, 36),
                Location = new Point((CardWidth - 120) / 2, 420),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = ColorTranslator.FromHtml("#3cb371"),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(0),
            };
            rent.FlatAppearance.BorderSize = 0;
            rent.Region = Rounded(rent.Size, 20);
            var filmId = film.Id;
            rent.Click += async (_, _) =>
            {
                bool rented;
                using (var form = _forms.Create<KiralamaForm>(_session, filmId))
                {
                    rented = form.ShowDialog(this) == DialogResult.OK;
                }

                if (rented)
                {
                    await this.RunAsync(LoadFilmsAsync);
                }
            };
            card.Controls.Add(rent);
            return card;
        }

        private static Region Rounded(Size size, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(size.Width - radius, 0, radius, radius, 270, 90);
            path.AddArc(size.Width - radius, size.Height - radius, radius, radius, 0, 90);
            path.AddArc(0, size.Height - radius, radius, radius, 90, 90);
            return new Region(path);
        }
    }
}
