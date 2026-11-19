using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>A main screen that the account can leave to sign in again.</summary>
    internal interface ISignOutPanel
    {
        /// <summary>Whether the panel was closed with its sign-out button.</summary>
        bool SignedOut { get; }
    }

    /// <summary>Sign-in; also the entry point to the sign-up form and, when no admin exists, the keyed setup.</summary>
    public partial class LoginForm : Form
    {
        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();

        public LoginForm(IAuthService auth, IFormFactory forms)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            pictureBox1.Image = AppAssets.Hero;
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            Text = AppText.Title;
            FormBorderStyle = FormBorderStyle.None;

            // The window has its own close and minimize buttons.
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            txtKullanici.MaxLength = InputRules.StoredUserNameMaxLength;
            txtSifre2.MaxLength = PasswordPolicy.MaxLength;

            var accent = ColorTranslator.FromHtml("#ea2842");
            btnOlustur2.ForeColor = accent;
            btnKurulum.ForeColor = accent;
            btnGiris2.PrimaryColor = accent;
            btnGiris2.Font = new Font(btnGiris2.Font.FontFamily, 9, FontStyle.Bold);
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup. That
        /// setup asks for a setup key issued by the database owner; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => btnKurulum.Tag is true;
            set
            {
                btnKurulum.Tag = value;
                btnKurulum.Visible = value;
            }
        }

        /// <summary>
        /// Signs the account in; ignored while a previous attempt is still running. Returns the session, or
        /// <see langword="null"/> when the attempt failed (the reason has been shown) or was ignored.
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
            Show();
            txtSifre2.Focus();
        }

        private async void btnGiris2_Click(object? sender, EventArgs e)
        {
            var session = await SignInAsync();
            if (session is not null)
            {
                OpenPanel(session);
            }
        }

        private void btnOlustur2_Click(object? sender, EventArgs e)
        {
            using var register = _forms.Create<RegisterForm>();
            register.StartPosition = FormStartPosition.CenterParent;
            register.ShowDialog(this);
        }

        private void btnKurulum_Click(object? sender, EventArgs e)
        {
            using var setup = _forms.Create<IlkKurulumForm>();
            setup.RequireSetupKey = true;
            setup.StartPosition = FormStartPosition.CenterParent;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                txtKullanici.Text = setup.Session?.UserName ?? string.Empty;
                txtSifre2.Focus();
            }
        }

        private void foxLabel4_MouseEnter(object? sender, EventArgs e) => btnOlustur2.Cursor = Cursors.Hand;

        private void foxLabel4_MouseLeave(object? sender, EventArgs e) => btnOlustur2.Cursor = Cursors.Default;

        private void button1_Click(object? sender, EventArgs e) => Close();

        private void button2_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;
    }
}
