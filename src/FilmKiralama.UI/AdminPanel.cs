using System.Globalization;
using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The admin's film management: search every film page by page, add, change and delete films, and open the
    /// rentals, accounts, reviews and reports. Signing out returns to the sign-in.
    /// </summary>
    public sealed class AdminPanel : AppDialog, ISignOutPanel
    {
        private readonly IFilmService _films;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly CatalogSearchBar _search = new(availableOnly: false);
        private readonly Pager _pager = new();
        private bool _binding;

        private readonly ThemedButton btnKiralanlar = FormLayout.Button(nameof(btnKiralanlar), "Kiralamalar", ButtonKind.Secondary, Glyphs.List);
        private readonly ThemedButton btnKullaniciPanel = FormLayout.Button(nameof(btnKullaniciPanel), "Hesaplar", ButtonKind.Secondary, Glyphs.People);
        private readonly ThemedButton btnYorumlar = FormLayout.Button(nameof(btnYorumlar), "Yorumlar", ButtonKind.Secondary, Glyphs.Comment);
        private readonly ThemedButton btnRaporlar = FormLayout.Button(nameof(btnRaporlar), "Raporlar", ButtonKind.Secondary, Glyphs.Chart);
        private readonly ThemedButton btnAracDurum = FormLayout.Button(nameof(btnAracDurum), "Yenile", ButtonKind.Subtle, Glyphs.Refresh);
        private readonly ThemedGrid dataGridView1 = new() { Name = nameof(dataGridView1) };

        private readonly PosterBox pictureBox1 = new() { Name = nameof(pictureBox1) };
        private readonly Label lblSecim = FormLayout.Hint(NewFilmText);
        private readonly TextBox txtAd = new() { Name = nameof(txtAd), MaxLength = InputRules.TitleMaxLength };
        private readonly TextBox txtTur = new() { Name = nameof(txtTur), MaxLength = InputRules.GenreMaxLength };
        private readonly ThemedComboBox cmbYil = FormLayout.Picker(nameof(cmbYil), 104, "Yıl");
        private readonly TextBox txtYonetmen = new() { Name = nameof(txtYonetmen), MaxLength = InputRules.DirectorMaxLength };
        private readonly TextBox txtUcret = new() { Name = nameof(txtUcret), MaxLength = 12 };
        private readonly NumericUpDown nudAdet = new() { Name = nameof(nudAdet), Minimum = InputRules.MinCopies, Maximum = InputRules.MaxCopies, Value = 1 };
        private readonly ThemedCheckBox chkKiralamayaAcik = new() { Name = nameof(chkKiralamayaAcik), Text = "Kiralamaya &açık", Checked = true };
        private readonly TextBox txtLink = new() { Name = nameof(txtLink), MaxLength = InputRules.ImageUrlMaxLength };
        private readonly TextBox txtHakkinda = new() { Name = nameof(txtHakkinda), Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = InputRules.AboutMaxLength };
        private readonly ThemedButton btnFilmEkle = FormLayout.Button(nameof(btnFilmEkle), "Film ekle", ButtonKind.Primary, Glyphs.Add);
        private readonly ThemedButton btnGuncelle = FormLayout.Button(nameof(btnGuncelle), "Güncelle", ButtonKind.Secondary, Glyphs.Save);
        private readonly ThemedButton btnFilmSil = FormLayout.Button(nameof(btnFilmSil), "Sil", ButtonKind.Danger, Glyphs.Delete);
        private readonly ThemedButton btnPanelCikis = FormLayout.Button(nameof(btnPanelCikis), "Çıkış yap", ButtonKind.Secondary, Glyphs.SignOut);
        private readonly ErrorProvider errorProvider1 = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        public AdminPanel(IFilmService films, IFormFactory forms, UserSession session)
            : base("Yönetim Paneli", "Filmleri arayın, ekleyin ve güncelleyin; kiralamalar, hesaplar, yorumlar ve raporlar buradan açılır.", Glyphs.Admin, new Size(1300, 820), new Size(1000, 640))
        {
            ArgumentNullException.ThrowIfNull(films);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _films = films;
            _forms = forms;
            _session = session;

            var thisYear = DateTime.Today.Year;
            for (var year = thisYear; year >= InputRules.MinYear; year--)
            {
                cmbYil.Items.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            txtUcret.AcceptMoneyOnly();
            BuildLayout();
            errorProvider1.ContainerControl = this;
            btnGuncelle.Enabled = false;

            _search.Changed += async (_, _) => await this.RunAsync(() => LoadFilmsAsync(1));
            _pager.PageRequested += async (_, page) => await this.RunAsync(() => LoadFilmsAsync(page));
            dataGridView1.CellClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is FilmRow row)
                {
                    SelectFilm(row.Film);
                }
            };
            dataGridView1.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user and changes nothing in the form.
                if (!_binding)
                {
                    btnGuncelle.Enabled = SelectedFilmId is not null;
                }
            };
            btnFilmEkle.Click += async (_, _) => await AddAsync();
            btnGuncelle.Click += async (_, _) => await UpdateAsync();
            btnFilmSil.Click += async (_, _) => await DeleteAsync();
            btnAracDurum.Click += async (_, _) => await this.RunAsync(RefreshAsync);
            btnKiralanlar.Click += async (_, _) => await OpenAsync<KiralananFilmlerPanel>(refresh: true);
            btnKullaniciPanel.Click += async (_, _) => await OpenAsync<KullaniciYonetimPanel>(refresh: false);
            btnYorumlar.Click += async (_, _) => await OpenAsync<YorumYonetimForm>(refresh: true);
            btnRaporlar.Click += async (_, _) => await OpenAsync<RaporlarForm>(refresh: false);
            btnPanelCikis.Click += (_, _) =>
            {
                SignedOut = true;
                Close();
            };
        }

        /// <summary>The editor's line while no film is chosen.</summary>
        internal const string NewFilmText = "Yeni film: alanları doldurup Film ekle'ye basın ya da tablodan bir film seçin.";

        public bool SignedOut { get; private set; }

        /// <summary>
        /// Shows the selected film's poster; only the poster of the latest selection is shown. Replaced in
        /// tests so no download starts.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal PosterLoader Posters { get; set; } = PosterLoader.Default;

        /// <summary>The film whose details are in the form, or <see langword="null"/> when a new one is entered.</summary>
        internal int? SelectedFilmId { get; private set; }

        internal IReadOnlyList<Film> ShownFilms { get; private set; } = [];

        /// <summary>The page of the search shown in the grid.</summary>
        internal PagedResult<CatalogItem>? ShownPage { get; private set; }

        internal CatalogSearchBar Search => _search;

        /// <summary>Loads the search choices and the shown page again.</summary>
        internal async Task RefreshAsync()
        {
            _search.Fill(await _films.GetFilterOptionsAsync(_session));
            await LoadFilmsAsync(ShownPage?.Page ?? 1);
        }

        /// <summary>
        /// Loads one page of the search into the grid; only the latest request is shown. Binding the rows
        /// selects nothing.
        /// </summary>
        internal async Task LoadFilmsAsync(int page = 1)
        {
            var request = _loads.Next();
            dataGridView1.IsLoading = true;
            try
            {
                var result = await _films.SearchAllAsync(_session, _search.Current with { Page = page, PageSize = CatalogQuery.MaxPageSize });
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownPage = result;
                var films = result.Items.Select(i => i.Film).ToList();
                ShownFilms = films;
                _pager.Show(result.Page, result.PageCount, result.TotalCount, "film");
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
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dataGridView1.IsLoading = false;
                }
            }
        }

        /// <summary>Shows a film's details in the form, as a click on its row does.</summary>
        internal void SelectFilm(Film film)
        {
            ArgumentNullException.ThrowIfNull(film);

            errorProvider1.Clear();
            SelectedFilmId = film.Id;
            txtAd.Text = film.Title;
            txtTur.Text = film.Genre;
            cmbYil.Text = film.Year.ToString(CultureInfo.InvariantCulture);
            txtYonetmen.Text = film.Director;
            txtUcret.Text = DisplayFormat.MoneyInput(film.DailyPrice);
            lblSecim.Text = string.Create(CultureInfo.CurrentCulture, $"Film no {film.Id} · {FilmRow.Describe(film)} · rafta {film.AvailableCopies} / {film.TotalCopies} kopya");
            nudAdet.Value = Math.Clamp(film.TotalCopies, (int)nudAdet.Minimum, (int)nudAdet.Maximum);
            chkKiralamayaAcik.Checked = film.IsListed;
            txtLink.Text = film.ImageUrl ?? string.Empty;
            txtHakkinda.Text = film.About ?? string.Empty;
            pictureBox1.AccessibleName = film.Title + " afişi";
            Posters.Show(pictureBox1, film.ImageUrl);
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
                await RefreshAsync();
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
                await RefreshAsync();
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
                await RefreshAsync();
            });
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                errorProvider1.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Opens another admin screen; returns and review changes alter the films' state and averages.</summary>
        private async Task OpenAsync<T>(bool refresh)
            where T : Form
        {
            if (_write.IsRunning)
            {
                return;
            }

            using (var form = _forms.Create<T>(_session))
            {
                form.ShowDialog(this);
            }

            if (refresh && !IsDisposed)
            {
                await this.RunAsync(RefreshAsync);
            }
        }

        /// <summary>Runs a film write exclusively; invalid fields are marked next to their controls.</summary>
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
            txtAd.Clear();
            txtTur.Clear();
            cmbYil.SelectedIndex = -1;
            cmbYil.Text = string.Empty;
            txtYonetmen.Clear();
            txtUcret.Clear();
            lblSecim.Text = NewFilmText;
            nudAdet.Value = 1;
            chkKiralamayaAcik.Checked = true;
            txtLink.Clear();
            txtHakkinda.Clear();
            Posters.Clear(pictureBox1);
            btnGuncelle.Enabled = false;
        }

        private void BuildLayout()
        {
            Name = nameof(AdminPanel);
            var p = Theme.Current;

            // The other screens, then a refresh of the films at the right.
            var toolbar = FormLayout.Bar(
                "toolbar",
                (btnKiralanlar, false),
                (btnKullaniciPanel, false),
                (btnYorumlar, false),
                (btnRaporlar, false),
                (FormLayout.Spacer(), true),
                (btnAracDurum, false));
            foreach (var button in new[] { btnKiralanlar, btnKullaniciPanel, btnYorumlar })
            {
                button.Margin = new Padding(0, 0, Spacing.S, 0);
            }

            btnRaporlar.Margin = new Padding(0, 0, Spacing.L, 0);
            btnAracDurum.Margin = Padding.Empty;
            toolbar.Margin = new Padding(0, 0, 0, Spacing.M);

            var searchCard = FormLayout.Card("searchCard", null, _search, fill: false);
            searchCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, Spacing.XS);
            searchCard.Margin = new Padding(0, 0, 0, Spacing.L);

            // Films: the table and its pages.
            dataGridView1.EmptyText = "Aramaya uyan film yok.";
            dataGridView1.HiddenColumns = new HashSet<string> { nameof(FilmRow.ImageUrl), nameof(FilmRow.About) };
            dataGridView1.ColumnHeaders = new Dictionary<string, string>
            {
                [nameof(FilmRow.Id)] = "No",
                [nameof(FilmRow.Title)] = "Film",
                [nameof(FilmRow.Genre)] = "Tür",
                [nameof(FilmRow.Year)] = "Yıl",
                [nameof(FilmRow.Director)] = "Yönetmen",
                [nameof(FilmRow.DailyPrice)] = "Ücret",
                [nameof(FilmRow.TotalCopies)] = "Adet",
                [nameof(FilmRow.AvailableCopies)] = "Rafta",
            };
            dataGridView1.FitColumns = new HashSet<string> { nameof(FilmRow.Id), nameof(FilmRow.Year), nameof(FilmRow.DailyPrice), nameof(FilmRow.TotalCopies), nameof(FilmRow.AvailableCopies) };
            dataGridView1.ColumnWeights = new Dictionary<string, float> { [nameof(FilmRow.Title)] = 170, [nameof(FilmRow.Status)] = 110 };
            dataGridView1.CellTone = (column, value) => column == nameof(FilmRow.Status) ? IzlemeListesiForm.StateColor(p, value as string) : null;
            var pages = FormLayout.Bar("pages", (FormLayout.Spacer(), true), (_pager, false));
            pages.Margin = new Padding(0, Spacing.M, 0, 0);
            var filmsCard = FormLayout.Card("filmsCard", "Filmler", FormLayout.Page("filmsPage", (dataGridView1, true), (pages, false)));
            filmsCard.Margin = Padding.Empty;

            // The editor: the poster beside the main fields, then price, copies, address and description.
            var title = FormLayout.Field("Film &adı", FormLayout.Input(txtAd));
            var genreYear = FormLayout.FieldRow("genreYearRow", (FormLayout.Field("&Tür", FormLayout.Input(txtTur)), 60), (FormLayout.Field("&Yıl", cmbYil), 40));
            var director = FormLayout.Field("Yö&netmen", FormLayout.Input(txtYonetmen));
            var main = FormLayout.Stack("mainFields", title, genreYear, director);
            title.Margin = new Padding(0, 0, 0, Spacing.M);
            director.Margin = Padding.Empty;
            pictureBox1.Width = Sizes.PosterThumbWidth + Spacing.XL;
            pictureBox1.Margin = new Padding(0, Spacing.XS, Spacing.L, 0);
            pictureBox1.AccessibleName = "Afiş";
            var top = new TableLayoutPanel
            {
                Name = "editorTop",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Spacing.M),
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pictureBox1.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            top.Controls.Add(pictureBox1, 0, 0);
            top.Controls.Add(main, 1, 0);

            txtUcret.AccessibleDescription = "Ondalık ayırıcı virgüldür.";
            var priceCopies = FormLayout.FieldRow(
                "priceRow",
                (FormLayout.Field("Günlük ü&cret (TL)", FormLayout.Input(txtUcret)), 40),
                (FormLayout.Field("A&det", FormLayout.Number(nudAdet)), 30),
                (FormLayout.FieldAction(chkKiralamayaAcik), 0));
            chkKiralamayaAcik.Margin = new Padding(0, Spacing.XS + 6, 0, 0);
            var link = FormLayout.Field("Afiş &bağlantısı", FormLayout.Input(txtLink), "Yalnızca herkese açık http veya https adresi.");
            link.Margin = new Padding(0, 0, 0, Spacing.M);
            var about = FormLayout.Field("Ha&kkında", FormLayout.Input(txtHakkinda, Sizes.NoteHeight));
            about.Margin = new Padding(0, 0, 0, Spacing.M);

            var actions = FormLayout.Bar("editorActions", (btnFilmEkle, false), (btnGuncelle, false), (FormLayout.Spacer(), true), (btnFilmSil, false));
            btnFilmEkle.Margin = new Padding(0, 0, Spacing.S, 0);
            btnGuncelle.Margin = Padding.Empty;
            btnFilmSil.Margin = Padding.Empty;
            actions.Margin = new Padding(0, Spacing.S, 0, 0);
            lblSecim.Name = nameof(lblSecim);
            lblSecim.Margin = new Padding(0, 0, 0, Spacing.M);
            var editor = FormLayout.Stack("editorForm", lblSecim, top, priceCopies, link, about, actions);
            var editorCard = FormLayout.Card("editorCard", "Film bilgileri", editor, fill: false);
            editorCard.Margin = Padding.Empty;
            var right = FormLayout.Page("editorColumn", (editorCard, false));

            Content.Controls.Add(FormLayout.Page(
                "adminPage",
                (toolbar, false),
                (searchCard, false),
                (FormLayout.Columns("layout", filmsCard, 60, right), true)));

            CancelButton = btnPanelCikis;
            SetFooterNote(_session.UserName + " olarak oturum açık. Kiralama geçmişi olan film silinemez; kiralamaya kapatılabilir.");
            SetFooterButtons(btnPanelCikis);
            FinishLayout();
        }
    }
}
