using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using FilmKiralama.UI.Theming;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>
/// Every screen is built with test doubles and laid out without being shown: window handles are created
/// for real layout and tab order, but no window becomes visible and no message window opens.
/// </summary>
public sealed class ScreenTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_HasTheApplicationIdentity(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        Assert.StartsWith(AppText.Title + " - ", form.Text, StringComparison.Ordinal);
        Assert.Same(AppAssets.Icon, form.Icon);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.True(form.MinimumSize.Width > 0 && form.MinimumSize.Height > 0, "A resizable screen needs a minimum size.");
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Null(form.BackgroundImage);
        Assert.False(form.Visible);
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_UsesThemedControlsAndAKeyboardWayOut(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);
        var controls = Descendants(form).ToList();

        var buttons = controls.OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.IsType<ThemedButton>(button));
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Text), $"{button.Name} has no text."));
        Assert.All(controls.OfType<ComboBox>(), combo => Assert.IsType<ThemedComboBox>(combo));
        Assert.All(controls.OfType<CheckBox>(), check => Assert.IsType<ThemedCheckBox>(check));
        Assert.All(controls.OfType<ListBox>(), list => Assert.IsType<ThemedListBox>(list));
        Assert.All(controls.OfType<DataGridView>(), grid => Assert.IsType<ThemedGrid>(grid));
        Assert.All(controls.OfType<NumericUpDown>(), number => Assert.IsType<NumberField>(number.Parent));
        Assert.All(controls.OfType<DateTimePicker>(), date => Assert.IsType<DateField>(date.Parent));
        Assert.All(controls.OfType<TextBox>().Where(box => box.Parent is not NumericUpDown), box => Assert.IsType<TextField>(box.Parent));
        Assert.All(controls.OfType<ThemedListBox>(), list => Assert.False(string.IsNullOrEmpty(list.EmptyText), $"{list.Name} has no empty text."));
        Assert.All(controls.OfType<ThemedGrid>(), grid => Assert.False(string.IsNullOrEmpty(grid.EmptyText), $"{grid.Name} has no empty text."));
        Assert.All(controls.OfType<CardGrid>(), grid => Assert.False(string.IsNullOrEmpty(grid.EmptyText), $"{grid.Name} has no empty text."));
        Assert.NotNull(form.CancelButton); // Esc closes every screen (a panel signs out).
    });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_LaysOutInsideItsWindow(string name) => Run(() =>
    {
        using var form = TestForms.Create(name);

        LayoutAssert.FitsInside(form);
        Assert.False(form.Visible);
    });

    public static TheoryData<string, int, int, float> ScreensOnSmallScreens
    {
        get
        {
            var data = new TheoryData<string, int, int, float>();
            foreach (var (width, height) in new[] { (1024, 600), (1366, 768) })
            {
                foreach (var scale in LayoutAssert.FontScales)
                {
                    foreach (var name in TestForms.Names)
                    {
                        data.Add(name, width, height, scale);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>
    /// On a small working area and with larger text the window still opens inside the screen, its minimum size
    /// too, and every control stays reachable: the body scrolls while the header and the footer stay in view,
    /// also while a two-line message is shown.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScreensOnSmallScreens))]
    public void Screen_FitsTheWorkingAreaAndKeepsEveryControlReachable(string name, int screenWidth, int screenHeight, float fontScale) => Run(() =>
    {
        var previous = Theme.Current;
        var previousScale = Theme.RenderScale;
        var previousArea = AppDialog.WorkingAreaOf;
        Theme.Use(previous, fontScale);
        var area = new Rectangle(0, 0, screenWidth, screenHeight);
        AppDialog.WorkingAreaOf = _ => area;
        try
        {
            using var form = TestForms.Create(name);
            LayoutAssert.CreateHandles(form);

            Assert.True(form.Width <= area.Width && form.Height <= area.Height, $"{name} opens at {form.Size}, larger than the working area {area.Size}.");
            Assert.True(form.MinimumSize.Width <= area.Width && form.MinimumSize.Height <= area.Height, $"{name} requires {form.MinimumSize}, larger than the working area {area.Size}.");

            foreach (var size in new[] { form.Size, form.MinimumSize })
            {
                form.Size = size;
                form.ShowStatus(StatusKind.Warning, "Lütfen bir film seçin." + Environment.NewLine + "İkinci satır.");
                form.PerformLayout();

                try
                {
                    LayoutAssert.FitsInside(form);
                }
                catch (Xunit.Sdk.XunitException ex)
                {
                    throw new Xunit.Sdk.XunitException($"{name} at {form.Size}: {ex.Message}");
                }

                form.ClearStatus();
            }
        }
        finally
        {
            AppDialog.WorkingAreaOf = previousArea;
            Theme.Use(previous, previousScale);
        }
    });

    [Theory]
    [InlineData(nameof(LoginForm), "txtKullanici", "btnCikis")]
    [InlineData(nameof(RegisterForm), "txtUsername", "btnKapat")]
    [InlineData(nameof(IlkKurulumForm), "txtAnahtar", "btnIptal")]
    [InlineData(nameof(SifreDegistirmeForm), "txtMevcutSifre", "btnKapat")]
    [InlineData(nameof(AnaForm), "btnOneriler", "btnCikis")]
    [InlineData(nameof(KiralamaForm), "btnAzalt", "btnKapat")]
    [InlineData(nameof(KullaniciProfil), "btnIade", "btnKapat")]
    [InlineData(nameof(DegerlendirmeForm), "cmbPuan", "btnKapat")]
    [InlineData(nameof(IzlemeListesiForm), "dgvListe", "btnKapat")]
    [InlineData(nameof(OnerilerForm), "dgvOneriler", "btnKapat")]
    [InlineData(nameof(AdminPanel), "btnKiralanlar", "btnPanelCikis")]
    [InlineData(nameof(KiralananFilmlerPanel), "cbTur", "btnKapat")]
    [InlineData(nameof(KullaniciYonetimPanel), "dataGridView1", "btnKapat")]
    [InlineData(nameof(YorumYonetimForm), "chkGizliler", "btnKapat")]
    [InlineData(nameof(RaporlarForm), "cmbRapor", "btnKapat")]
    public void TabOrder_StartsAtTheFirstInputAndEndsInTheFooter(string name, string first, string last) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var order = TabOrder(form);

        Assert.Equal(first, order[0].Name);
        Assert.Equal(last, order[^1].Name);
        Assert.Equal(order.Count, order.Distinct().Count());
    });

    [Theory]
    [InlineData(nameof(LoginForm), new[] { "txtKullanici", "txtSifre2", "btnGiris2", "btnOlustur2", "btnKurulum", "btnCikis" })]
    [InlineData(nameof(IlkKurulumForm), new[] { "txtAnahtar", "txtKullaniciAdi", "txtSifre", "txtSifreTekrar", "btnOlustur", "btnIptal" })]
    [InlineData(nameof(AnaForm), new[] { "btnOneriler", "btnIzlemeListesi", "btnProfil", "txtAra", "turCombo", "yilBaslangicCombo", "yilBitisCombo", "puanCombo", "chkMusait", "btnKirala", "btnListeyeEkle", "btnCikis" })]
    [InlineData(nameof(KiralamaForm), new[] { "btnAzalt", "txtGunSayisi", "btnArtir", "btnOdemeyeGec", "btnKapat" })]
    [InlineData(nameof(AdminPanel), new[] { "btnKiralanlar", "txtAra", "dataGridView1", "txtAd", "txtTur", "cmbYil", "txtYonetmen", "txtUcret", "nudAdet", "chkKiralamayaAcik", "txtLink", "txtHakkinda", "btnFilmEkle", "btnGuncelle", "btnFilmSil", "btnPanelCikis" })]
    [InlineData(nameof(RaporlarForm), new[] { "cmbRapor", "dtpBaslangic", "dtpBitis", "btnGoster", "dgvRapor", "btnCsv", "btnKapat" })]
    public void TabOrder_FollowsTheReadingOrder(string name, string[] expected) => Run(() =>
    {
        using var form = TestForms.Create(name);
        LayoutAssert.CreateHandles(form);

        var names = TabOrder(form).Select(control => control.Name).Where(expected.Contains).Distinct().ToList();

        Assert.Equal(expected, names);
    });

    [Fact]
    public void FieldCaptions_NameTheirInputs() => Run(() =>
    {
        using var form = TestForms.Create(nameof(LoginForm));

        Assert.Equal("Kullanıcı adı", Find<TextBox>(form, "txtKullanici").AccessibleName);
        Assert.Equal("Şifre", Find<TextBox>(form, "txtSifre2").AccessibleName);
        Assert.False(Descendants(form).OfType<TextField>().Any(field => field.CanSelect), "A text field frame must hand the focus to its text box.");

        using var reports = TestForms.Create(nameof(RaporlarForm));
        Assert.Equal("Başlangıç", Find<DateTimePicker>(reports, "dtpBaslangic").AccessibleName);
    });

    [Fact]
    public void Validation_IsShownInlineWithoutADialog() => Run(() =>
    {
        using var form = TestForms.Watchlist();
        var dialogs = NoDialogs.Requested.Count;
        form.Prompts = new ScreenPrompts(form);

        Assert.True(form.RemoveAsync().IsCompletedSuccessfully);

        Assert.True(form.StatusBar.IsShown);
        Assert.Equal(StatusKind.Warning, form.StatusBar.Kind);
        Assert.Equal("Lütfen listeden bir film seçin.", form.StatusBar.Text);
        Assert.Equal(form.StatusBar.Text, form.StatusBar.AccessibleName);
        Assert.Equal(dialogs, NoDialogs.Requested.Count);
    });

    /// <summary>A refusal of the service (a business rule) appears in the screen's message strip, not in a window.</summary>
    [Fact]
    public void RuleRefusal_IsShownInline() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        var dialogs = NoDialogs.Requested.Count;

        var signIn = form.SignInAsync();
        auth.RefuseSignIn();

        Assert.True(signIn.IsCompletedSuccessfully);
        Assert.Equal((StatusKind.Warning, "Kullanıcı adı veya şifre hatalı."), (form.StatusBar.Kind, form.StatusBar.Text));
        Assert.Equal(dialogs, NoDialogs.Requested.Count);
    });

    [Fact]
    public void NextWrite_ClearsThePreviousMessage() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        var first = form.SignInAsync();
        auth.RefuseSignIn();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(form.StatusBar.IsShown);

        _ = form.SignInAsync(); // Never completes; the old warning must be gone meanwhile.

        Assert.Equal(2, auth.SignInCalls);
        Assert.False(form.StatusBar.IsShown);
    });

    [Fact]
    public void ReturnQuestion_GoesThroughTheDialogHelper() => Run(() =>
    {
        using var form = TestForms.Profile();
        form.Prompts = new ScreenPrompts(form);
        var before = NoDialogs.Requested.Count;

        Assert.True(form.ReturnAsync(form.ShownRentals[1]).IsCompletedSuccessfully);

        Assert.Equal("Onay: \"Sisli Liman\" iade edilecek.\n\nİade etmek istiyor musunuz?", Assert.Single(NoDialogs.Requested.Skip(before)));
    });

    /// <summary>The catalog says while it loads, and says why it is empty.</summary>
    [Fact]
    public void Catalog_SaysWhenItLoadsAndWhenNothingWasFound() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AnaForm(films, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<CardGrid>(form, "pnlFilmler");

        _ = form.LoadFilmsAsync();
        Assert.True(grid.IsLoading);
        Assert.Equal(grid.LoadingText, grid.ShownMessage);
        films.CatalogRequests[0].Answer.SetResult(ControlledFilmService.Page([]));

        Assert.False(grid.IsLoading);
        Assert.Equal(grid.EmptyText, grid.ShownMessage);
    });

    /// <summary>Tables say while they load and when they are empty, and binding their rows selects none.</summary>
    [Fact]
    public void Tables_SayWhenTheyLoadAndWhenTheyAreEmpty() => Run(() =>
    {
        var reviews = new ControlledReviewService();
        using var form = new YorumYonetimForm(reviews, Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<ThemedGrid>(form, "dgvYorumlar");
        _ = grid.Handle;

        _ = form.LoadAsync(1);
        Assert.Equal(grid.LoadingText, grid.ShownMessage);
        reviews.ListRequests[0].Answer.SetResult(new PagedResult<ReviewDetails>([], 0, 1, 50));

        Assert.Equal(grid.EmptyText, grid.ShownMessage);
        Assert.Empty(grid.SelectedRows);
    });

    /// <summary>A card shows the stock as a pill on its poster: out of stock, the last copy, or nothing.</summary>
    [Fact]
    public void PosterCard_RentsOnlyFilmsOnTheShelf() => Run(() =>
    {
        var films = TestForms.Films();
        using var empty = new PosterCard(new CatalogItem(films.Films[1], null, 0));
        using var available = new PosterCard(new CatalogItem(films.Films[0], 4.3m, 12));

        Assert.False(empty.Rent.Enabled);
        Assert.Equal("Stokta yok", empty.Rent.Text);
        Assert.True(available.Rent.Enabled);
        Assert.Equal("Listeye ekle", available.AddToList.Text);
        Assert.Equal(TestForms.LongTitle, available.AccessibleName);
    });

    /// <summary>A long title takes at most two lines on a card and ends in an ellipsis; a short one stays whole.</summary>
    [Fact]
    public void PosterCard_LongTitlesEndInAnEllipsis() => Run(() =>
    {
        var font = Theme.Fonts.Subtitle;

        var fitted = PosterCard.FitLines(TestForms.LongTitle, font, 120, 2);

        Assert.EndsWith("…", fitted, StringComparison.Ordinal);
        Assert.True(TextRenderer.MeasureText(fitted, font, new Size(120, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height <= 2 * font.Height);
        Assert.Equal("Son Vapur", PosterCard.FitLines("Son Vapur", font, 120, 2));
    });

    [Fact]
    public void MessageDialog_AnswersWithKeysAndIsNeverShown() => Run(() =>
    {
        using var dialog = new MessageDialog("Seçili film silinsin mi?", "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        Assert.False(dialog.Visible);
        Assert.Equal(DialogResult.Yes, Assert.IsType<ThemedButton>(dialog.AcceptButton).DialogResult);
        Assert.Equal(DialogResult.No, Assert.IsType<ThemedButton>(dialog.CancelButton).DialogResult);
        Assert.Equal(AppText.Title, dialog.Text);
        Assert.True(dialog.ClientSize.Width > 0 && dialog.ClientSize.Height > 0);
        Assert.Equal("Bir sorun oluştu", MessageDialog.Heading(AppText.Title, MessageBoxIcon.Error));
        Assert.Equal("Uyarı", MessageDialog.Heading("Uyarı", MessageBoxIcon.Warning));
    });

    /// <summary>The sign-in says why the previous session ended, or that the first admin was created.</summary>
    [Fact]
    public void SignIn_ShowsANotice() => Run(() =>
    {
        using var form = new LoginForm(new PendingAuthService(), new NoFormsFactory());

        form.ShowNotice(LoginForm.AdminCreatedNotice);

        Assert.Equal((StatusKind.Info, LoginForm.AdminCreatedNotice), (form.StatusBar.Kind, form.StatusBar.Text));
    });

    /// <summary>The hidden notice of a review takes room only while the review is hidden.</summary>
    [Fact]
    public void ReviewForm_HiddenNoticeTakesRoomOnlyWhenHidden() => Run(() =>
    {
        using var hidden = TestForms.Review();
        using var visible = new DegerlendirmeForm(new ControlledReviewService(), Sessions.User, new ReviewableFilm(1, "Sisli Liman", "Gerilim", 2019, null, null));

        Assert.False(FormLayout.IsHidden(Find<Control>(hidden, "hiddenNote")));
        Assert.True(FormLayout.IsHidden(Find<Control>(visible, "hiddenNote")));
    });

    internal static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Focusable controls in the order Tab visits them.</summary>
    private static List<Control> TabOrder(Form form)
    {
        var order = new List<Control>();
        Control? current = null;
        while ((current = form.GetNextControl(current, forward: true)) is not null)
        {
            if (CanSelectWhenShown(current))
            {
                order.Add(current);
            }
        }

        return order;
    }

    /// <summary>
    /// Whether Tab can stop on the control once the window is shown: an enabled tab stop that is not a
    /// container, label, frame or badge, not hidden by its screen and not the inner part of another control.
    /// </summary>
    private static bool CanSelectWhenShown(Control control) =>
        control.TabStop
        && control.Enabled
        && !FormLayout.IsHidden(control)
        && control.Parent is not (NumericUpDown or DataGridView)
        && control is not (Label or Panel or TableLayoutPanel or FlowLayoutPanel or TextField or NumberField or DateField or GlyphBadge or InfoBar or BrandRule or PictureBox);
}
