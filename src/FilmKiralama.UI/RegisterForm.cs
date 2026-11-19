using System.Globalization;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Sign-up of a new customer account. Closes with <see cref="DialogResult.OK"/> once the account was
    /// stored; the sign-in then says so and fills in the user name.
    /// </summary>
    public sealed class RegisterForm : AppDialog
    {
        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _register = new();

        private readonly TextBox txtUsername = new() { Name = nameof(txtUsername), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtPassword = new() { Name = nameof(txtPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtMail = new() { Name = nameof(txtMail), MaxLength = InputRules.EmailMaxLength };
        private readonly TextBox txtTelefon = new() { Name = nameof(txtTelefon), MaxLength = 12 };
        private readonly ThemedButton btnKayit = FormLayout.Button(nameof(btnKayit), "Kayıt ol", ButtonKind.Primary);

        public RegisterForm(IAuthService auth)
            : base("Kayıt Ol", "Kiralamaya başlamak için bir müşteri hesabı açın.", Glyphs.AddPerson, new Size(600, 460), new Size(480, 420), modal: true)
        {
            ArgumentNullException.ThrowIfNull(auth);

            _auth = auth;
            txtTelefon.FormatAsPhone();
            BuildLayout();
            _register.HoldClosingOf(this);
            btnKayit.Click += async (_, _) => await RegisterAndCloseAsync();
        }

        /// <summary>The user name of the account just registered, for the sign-in.</summary>
        internal string? RegisteredUserName { get; private set; }

        /// <summary>Registers the account; ignored while a previous attempt is still running. Returns whether it was stored.</summary>
        internal async Task<bool> RegisterAsync()
        {
            var registered = false;
            await this.RunExclusiveAsync(
                _register,
                async () =>
                {
                    try
                    {
                        await _auth.RegisterAsync(txtUsername.Text, txtPassword.Text, txtMail.Text, txtTelefon.Text);
                        registered = true;
                        RegisteredUserName = txtUsername.Text.Trim();
                    }
                    finally
                    {
                        txtPassword.Clear();
                        if (!registered)
                        {
                            _register.FocusAfterUnlock(txtUsername);
                        }
                    }
                },
                btnKayit);
            return registered;
        }

        private async Task RegisterAndCloseAsync()
        {
            if (await RegisterAsync() && !IsDisposed)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(RegisterForm);
            var name = FormLayout.Field(
                "&Kullanıcı adı",
                FormLayout.Input(txtUsername),
                string.Create(CultureInfo.CurrentCulture, $"En az {InputRules.UserNameMinLength}, en fazla {InputRules.UserNameMaxLength} karakter."));
            var password = FormLayout.Field(
                "Ş&ifre",
                FormLayout.Input(txtPassword),
                string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter."));
            var contact = FormLayout.FieldRow(
                "contactRow",
                (FormLayout.Field("&E-posta", FormLayout.Input(txtMail)), 58),
                (FormLayout.Field("&Telefon", FormLayout.Input(txtTelefon), "555 123 4567"), 42));
            name.Margin = password.Margin = new Padding(0, 0, 0, Spacing.M);
            var card = FormLayout.Card("signUpCard", null, FormLayout.Stack("signUpForm", name, password, contact), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            AcceptButton = btnKayit;
            SetFooterNote("Kayıttan sonra giriş ekranında yeni hesabınızla oturum açın.");
            SetFooterButtons(btnKayit, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
