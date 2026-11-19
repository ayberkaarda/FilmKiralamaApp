using System.Globalization;
using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>The admin's film management: list, add, change and delete films, and the other admin panels.</summary>
    public partial class AdminPanel : Form, ISignOutPanel
    {
        private readonly IFilmService _films;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        public AdminPanel(IFilmService films, IFormFactory forms, UserSession session)
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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            var thisYear = DateTime.Today.Year;
            for (var year = thisYear; year >= InputRules.MinYear; year--)
            {
                cmbYil.Items.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            txtAd.MaxLength = InputRules.TitleMaxLength;
            txtTur.MaxLength = InputRules.GenreMaxLength;
            txtYonetmen.MaxLength = InputRules.DirectorMaxLength;
            txtLink.MaxLength = InputRules.ImageUrlMaxLength;
            txtHakkinda.MaxLength = InputRules.AboutMaxLength;
            txtUcret.MaxLength = 12;
            txtUcret.AcceptMoneyOnly();
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            dataGridView1.UseReadOnlyList();
            btnGuncelle.Enabled = false;
        }

        public bool SignedOut { get; private set; }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The film whose details are in the form, or <see langword="null"/> when a new one is entered.</summary>
        internal int? SelectedFilmId { get; private set; }

        internal IReadOnlyList<Film> ShownFilms { get; private set; } = [];

        /// <summary>Loads the film grid; only the latest request is shown. Binding the rows selects nothing.</summary>
        internal async Task LoadFilmsAsync()
        {
            var request = _loads.Next();
            var films = await _films.GetAllAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownFilms = films;
            _binding = true;
            try
            {
                dataGridView1.DataSource = films.Select(f => new FilmRow(f)).ToList();
                dataGridView1.ClearSelection();
            }
            finally
            {
                _binding = false;
            }

            btnGuncelle.Enabled = SelectedFilmId is not null;
        }

        /// <summary>Shows a film's details in the form, as a click on its row does.</summary>
        internal void SelectFilm(Film film)
        {
            ArgumentNullException.ThrowIfNull(film);

            errorProvider1.Clear();
            SelectedFilmId = film.Id;
            txtID.Text = film.Id.ToString(CultureInfo.InvariantCulture);
            txtAd.Text = film.Title;
            txtTur.Text = film.Genre;
            cmbYil.Text = film.Year.ToString(CultureInfo.InvariantCulture);
            txtYonetmen.Text = film.Director;
            txtUcret.Text = DisplayFormat.MoneyInput(film.DailyPrice);
            txtDurum.Text = $"{FilmRow.Describe(film)} ({film.AvailableCopies}/{film.TotalCopies})";
            nudAdet.Value = Math.Clamp(film.TotalCopies, (int)nudAdet.Minimum, (int)nudAdet.Maximum);
            chkKiralamayaAcik.Checked = film.IsListed;
            txtLink.Text = film.ImageUrl ?? string.Empty;
            txtHakkinda.Text = film.About ?? string.Empty;
            pictureBox1.ShowPoster(film.ImageUrl);
            btnGuncelle.Enabled = true;
        }

        /// <summary>
        /// Adds the entered film. While a film is selected the first click only clears the form, as in the
        /// original panel. Ignored while another write is running.
        /// </summary>
        internal async Task AddAsync()
        {
            if (SelectedFilmId is not null)
            {
                ClearForm();
                Prompts.Inform("Bir kayıt seçili durumda. Yeni film eklemek için seçimler kaldırıldı. Şimdi ekleyebilirsiniz.");
                return;
            }

            await WriteAsync(async () =>
            {
                await _films.AddAsync(_session, ReadInput());
                Prompts.Inform("Film başarıyla eklendi.");
                ClearForm();
                await LoadFilmsAsync();
            });
        }

        internal async Task UpdateAsync()
        {
            if (SelectedFilmId is not { } id)
            {
                Prompts.Warn("Lütfen güncellemek için bir film seçin.");
                return;
            }

            await WriteAsync(async () =>
            {
                await _films.UpdateAsync(_session, id, ReadInput());
                Prompts.Inform("Film başarıyla güncellendi!");
                await LoadFilmsAsync();
                if (ShownFilms.FirstOrDefault(f => f.Id == id) is { } updated)
                {
                    SelectFilm(updated);
                }
            });
        }

        internal async Task DeleteAsync()
        {
            if (SelectedFilmId is not { } id)
            {
                Prompts.Warn("Lütfen silmek için bir film seçin.");
                return;
            }

            await WriteAsync(async () =>
            {
                if (!Prompts.Confirm("Seçili filmi silmek istediğinize emin misiniz?"))
                {
                    return;
                }

                await _films.DeleteAsync(_session, id);
                Prompts.Inform("Film başarıyla silindi.");
                ClearForm();
                await LoadFilmsAsync();
            });
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadFilmsAsync);
        }

        /// <summary>Runs a film write exclusively; invalid fields are marked as the original form marked empty ones.</summary>
        private Task<bool> WriteAsync(Func<Task> write) =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    errorProvider1.Clear();
                    try
                    {
                        await write();
                    }
                    catch (BusinessRuleException ex) when (ex.FieldErrors.Count > 0)
                    {
                        MarkFields(ex.FieldErrors);
                        _write.FocusAfterUnlock(FieldControl(ex.FieldErrors.Keys.First()));
                        throw;
                    }
                },
                btnFilmEkle,
                btnGuncelle,
                btnFilmSil,
                btnAracDurum);

        private FilmInput ReadInput() => new(
            txtAd.Text,
            txtTur.Text,
            cmbYil.Text,
            txtYonetmen.Text,
            txtUcret.Text,
            txtLink.Text,
            txtHakkinda.Text,
            (int)nudAdet.Value,
            chkKiralamayaAcik.Checked);

        private void MarkFields(IReadOnlyDictionary<string, string> errors)
        {
            foreach (var (field, message) in errors)
            {
                if (FieldControl(field) is { } control)
                {
                    errorProvider1.SetError(control, message);
                }
            }
        }

        private Control? FieldControl(string field) => field switch
        {
            nameof(FilmInput.Title) => txtAd,
            nameof(FilmInput.Genre) => txtTur,
            nameof(FilmInput.Year) => cmbYil,
            nameof(FilmInput.Director) => txtYonetmen,
            nameof(FilmInput.DailyPrice) => txtUcret,
            nameof(FilmInput.ImageUrl) => txtLink,
            nameof(FilmInput.About) => txtHakkinda,
            nameof(FilmInput.Copies) => nudAdet,
            _ => null,
        };

        private void ClearForm()
        {
            SelectedFilmId = null;
            dataGridView1.ClearSelection();
            errorProvider1.Clear();
            txtID.Clear();
            txtAd.Clear();
            txtTur.Clear();
            cmbYil.SelectedIndex = -1;
            cmbYil.Text = string.Empty;
            txtYonetmen.Clear();
            txtUcret.Clear();
            txtDurum.Clear();
            nudAdet.Value = 1;
            chkKiralamayaAcik.Checked = true;
            txtLink.Clear();
            txtHakkinda.Clear();
            pictureBox1.CancelAsync();
            pictureBox1.Image = null;
            btnGuncelle.Enabled = false;
        }

        private async void btnAracDurum_Click(object? sender, EventArgs e) => await this.RunAsync(LoadFilmsAsync);

        private void btnKullaniciPanel_Click(object? sender, EventArgs e)
        {
            using var panel = _forms.Create<KullaniciYonetimPanel>(_session);
            panel.ShowDialog(this);
        }

        private async void btnKiralanlar_Click(object? sender, EventArgs e)
        {
            using (var panel = _forms.Create<KiralananFilmlerPanel>(_session))
            {
                panel.ShowDialog(this);
            }

            // Returns change the copies on the shelf.
            await this.RunAsync(LoadFilmsAsync);
        }

        private void dataGridView1_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is FilmRow row)
            {
                SelectFilm(row.Film);
            }
        }

        private void dataGridView1_SelectionChanged(object? sender, EventArgs e)
        {
            // Binding new rows is not a choice of the user and changes nothing in the form.
            if (!_binding)
            {
                btnGuncelle.Enabled = SelectedFilmId is not null;
            }
        }

        private async void btnAracEkle_Click(object? sender, EventArgs e) => await AddAsync();

        private async void btnGuncelle_Click_1(object? sender, EventArgs e) => await UpdateAsync();

        private async void btnAracSil_Click(object? sender, EventArgs e) => await DeleteAsync();

        private void btnPanelCikis_Click(object? sender, EventArgs e)
        {
            SignedOut = true;
            Close();
        }
    }
}
