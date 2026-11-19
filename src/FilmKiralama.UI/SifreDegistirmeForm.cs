using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>Change of the signed-in account's own password.</summary>
    public partial class SifreDegistirmeForm : Form
    {
        private readonly IAuthService _auth;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();

        public SifreDegistirmeForm(IAuthService auth, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(session);

            _auth = auth;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;
            foreach (var box in new[] { txtMevcutSifre, txtYeniSifre, txtYeniSifreTekrar })
            {
                box.MaxLength = PasswordPolicy.MaxLength;
            }

            AcceptButton = btnSifreDegistir;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Changes the password; ignored while a previous attempt is still running. Returns whether it changed.</summary>
        internal async Task<bool> ChangeAsync()
        {
            var changed = false;
            await this.RunExclusiveAsync(
                _change,
                async () =>
                {
                    try
                    {
                        await _auth.ChangePasswordAsync(_session, txtMevcutSifre.Text, txtYeniSifre.Text, txtYeniSifreTekrar.Text);
                        changed = true;
                    }
                    finally
                    {
                        txtMevcutSifre.Clear();
                        if (!changed)
                        {
                            _change.FocusAfterUnlock(txtMevcutSifre);
                        }
                    }
                },
                btnSifreDegistir);
            return changed;
        }

        private async void btnSifreDegistir_Click(object? sender, EventArgs e)
        {
            if (await ChangeAsync())
            {
                txtYeniSifre.Clear();
                txtYeniSifreTekrar.Clear();
                Prompts.Inform("Şifre başarıyla değiştirildi.");
                Close();
            }
        }
    }
}
