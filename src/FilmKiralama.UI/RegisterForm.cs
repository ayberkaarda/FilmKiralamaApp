using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>Sign-up of a new customer account.</summary>
    public partial class RegisterForm : Form
    {
        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _register = new();

        public RegisterForm(IAuthService auth)
        {
            ArgumentNullException.ThrowIfNull(auth);

            _auth = auth;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            txtUsername.MaxLength = InputRules.UserNameMaxLength;
            txtPassword.MaxLength = PasswordPolicy.MaxLength;
            txtMail.MaxLength = InputRules.EmailMaxLength;
            txtTelefon.MaxLength = 12;
            txtTelefon.FormatAsPhone();
            AcceptButton = btnKayit;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Registers the account; ignored while a previous attempt is still running. Returns whether it was stored.</summary>
        internal async Task<bool> RegisterAsync()
        {
            MarkEmptyFields();
            var registered = false;
            await this.RunExclusiveAsync(
                _register,
                async () =>
                {
                    try
                    {
                        await _auth.RegisterAsync(txtUsername.Text, txtPassword.Text, txtMail.Text, txtTelefon.Text);
                        registered = true;
                    }
                    finally
                    {
                        if (!registered)
                        {
                            _register.FocusAfterUnlock(txtUsername);
                        }
                    }
                },
                btnKayit);
            return registered;
        }

        /// <summary>Marks the empty fields as the original form did; the service decides what is accepted.</summary>
        private void MarkEmptyFields()
        {
            if (SystemInformation.HighContrast)
            {
                return;
            }

            foreach (var box in new[] { txtUsername, txtPassword, txtMail, txtTelefon })
            {
                box.BackColor = string.IsNullOrWhiteSpace(box.Text) ? Color.LightPink : Color.White;
            }
        }

        private async void btnKayit_Click(object? sender, EventArgs e)
        {
            if (await RegisterAsync())
            {
                Prompts.Inform("Kayıt başarılı!");
                Close();
            }
        }
    }
}
