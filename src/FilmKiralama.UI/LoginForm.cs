using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>A main screen that the account can leave to sign in again.</summary>
    internal interface ISignOutPanel
    {
        /// <summary>Whether the panel was closed with its sign-out button.</summary>
        bool SignedOut { get; }
    }

    /// <summary>
    /// Sign-in with a brand panel; also the entry point to the sign-up and, while the database has accounts but
    /// no admin, to the keyed setup of the first admin. It stays the application's main window: it hides while
    /// a panel is open and returns when the account signs out.
    /// </summary>
    public sealed class LoginForm : AppDialog
    {
        /// <summary>Shown when a panel was closed with its sign-out button.</summary>
        internal const string SignedOutNotice = "Oturumunuz kapatıldı. Devam etmek için yeniden giriş yapın.";

        /// <summary>Shown after the first admin was created.</summary>
        internal const string AdminCreatedNotice = "Yönetici hesabı oluşturuldu. Yeni şifrenizle giriş yapın.";

        /// <summary>Shown after a new account was registered.</summary>
        internal const string RegisteredNotice = "Kayıt başarılı. Yeni hesabınızla giriş yapabilirsiniz.";

        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();
        private bool _offerAdminSetup;

        private readonly TextBox txtKullanici = new() { Name = nameof(txtKullanici), MaxLength = InputRules.StoredUserNameMaxLength };
        private readonly TextBox txtSifre2 = new() { Name = nameof(txtSifre2), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnGiris2 = FormLayout.Button(nameof(btnGiris2), "Giriş yap", ButtonKind.Primary);
        private readonly ThemedButton btnOlustur2 = FormLayout.Button(nameof(btnOlustur2), "Hesap oluştur", ButtonKind.Secondary, Glyphs.AddPerson);
        private readonly ThemedButton btnKurulum = FormLayout.Button(nameof(btnKurulum), "İlk yönetici hesabını oluştur", ButtonKind.Subtle, Glyphs.Admin);

        public LoginForm(IAuthService auth, IFormFactory forms)
            : base("Giriş", string.Empty, string.Empty, new Size(880, 560), new Size(760, 500), header: false)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;
            BuildLayout();
            OfferAdminSetup = false;

            btnGiris2.Click += async (_, _) => await SignInAndOpenPanelAsync();
            btnOlustur2.Click += (_, _) => OpenSignUp();
            btnKurulum.Click += (_, _) => OpenSetup();
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup. That
        /// setup asks for a setup key issued by the database owner; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => _offerAdminSetup;
            set
            {
                _offerAdminSetup = value;
                FormLayout.SetShown(btnKurulum, value);
            }
        }

        /// <summary>A message for the next sign-in, for example after the first admin was created.</summary>
        internal void ShowNotice(string? notice)
        {
            if (!string.IsNullOrWhiteSpace(notice))
            {
                ShowStatus(StatusKind.Info, notice);
            }
        }

        /// <summary>
        /// Signs the account in; ignored while a previous attempt is still running. Returns the session, or
        /// <see langword="null"/> when the attempt failed (the reason is shown in the window) or was ignored.
        /// </summary>
        internal async Task<UserSession?> SignInAsync()
        {
            UserSession? session = null;
            await this.RunExclusiveAsync(
                _signIn,
                async () =>
                {
                    try
                    {
                        session = await _auth.SignInAsync(txtKullanici.Text, txtSifre2.Text);
                    }
                    finally
                    {
                        txtSifre2.Text = string.Empty;
                        if (session is null)
                        {
                            // Retyping the password is the next step after a refused attempt.
                            _signIn.FocusAfterUnlock(txtSifre2);
                        }
                    }
                },
                btnGiris2,
                btnOlustur2,
                btnKurulum);
            return session;
        }

        private async Task SignInAndOpenPanelAsync()
        {
            var session = await SignInAsync();
            if (session is not null && !IsDisposed)
            {
                OpenPanel(session);
            }
        }

        /// <summary>
        /// Opens the screen of the signed-in role: admins get the management panel, users the film list.
        /// Signing out there returns to this form; closing the panel ends the application, as before.
        /// </summary>
        private void OpenPanel(UserSession session)
        {
            Hide();
            bool signedOut;
            using (Form panel = session.IsAdmin ? _forms.Create<AdminPanel>(session) : _forms.Create<AnaForm>(session))
            {
                panel.ShowDialog();
                signedOut = panel is ISignOutPanel { SignedOut: true };
            }

            if (!signedOut)
            {
                Close();
                return;
            }

            txtSifre2.Text = string.Empty;
            ShowNotice(SignedOutNotice);
            Show();
            txtSifre2.Focus();
        }

        private void OpenSignUp()
        {
            if (_signIn.IsRunning)
            {
                return;
            }

            using var register = _forms.Create<RegisterForm>();
            if (register.ShowDialog(this) == DialogResult.OK)
            {
                txtKullanici.Text = register.RegisteredUserName ?? string.Empty;
                ShowNotice(RegisteredNotice);
                txtSifre2.Focus();
            }
        }

        private void OpenSetup()
        {
            if (_signIn.IsRunning)
            {
                return;
            }

            using var setup = _forms.Create<IlkKurulumForm>();
            setup.RequireSetupKey = true;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                txtKullanici.Text = setup.Session?.UserName ?? string.Empty;
                ShowNotice(AdminCreatedNotice);
                txtSifre2.Focus();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(LoginForm);
            SetSidePanel(
                new BrandPanel(
                    "Kataloğu keşfedin, filmi birkaç tıkla kiralayın, izlediklerinizi puanlayın.",
                    (Glyphs.Search, "Ada, türe, yıla ve puana göre arayın; rafta kopyası olan filmler hemen kiralanır."),
                    (Glyphs.Watchlist, "Beğendiğiniz filmleri izleme listenize ekleyin, müsait olunca kiralayın."),
                    (Glyphs.Star, "İade ettiğiniz filmleri puanlayın; zevkinize göre öneriler alın.")),
                Sizes.BrandPanelWidth);

            var title = FormLayout.SectionTitle("Tekrar hoş geldiniz");
            title.Name = "formTitle";
            title.Font = Theme.Fonts.Title;
            var intro = FormLayout.Hint("Kullanıcı adınız ve şifrenizle oturum açın.");
            intro.Name = "formIntro";
            intro.Margin = new Padding(0, 0, 0, Spacing.XL);

            var name = FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtKullanici));
            var password = FormLayout.Field("Ş&ifre", FormLayout.Input(txtSifre2));
            name.Margin = password.Margin = new Padding(0, 0, 0, Spacing.M);
            btnGiris2.AutoSize = false;
            btnGiris2.Dock = DockStyle.Top;
            btnGiris2.Margin = new Padding(0, Spacing.S, 0, Spacing.XL);

            var newHere = FormLayout.Hint("Hesabınız yok mu? Birkaç bilgiyle hemen bir müşteri hesabı açın.");
            newHere.Name = "signUpHint";
            newHere.Margin = new Padding(0, 0, 0, Spacing.S);
            btnOlustur2.Margin = new Padding(0, 0, 0, Spacing.L);
            btnKurulum.Margin = Padding.Empty;

            var stack = FormLayout.Stack("signInForm", title, intro, name, password, btnGiris2, newHere, btnOlustur2, btnKurulum);
            stack.Padding = new Padding(Spacing.S, Spacing.S, Spacing.S, 0);
            btnOlustur2.Anchor = btnKurulum.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            Content.Controls.Add(stack);

            AcceptButton = btnGiris2;
            SetFooterNote("Yönetici hesapları, yönetim panelindeki kullanıcı yönetiminden verilir.");
            SetFooterButtons(CreateCloseButton("btnCikis", "Çıkış"));
            FinishLayout();
        }
    }
}
