using System.Globalization;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// Change of the signed-in account's own password. Closes with <see cref="DialogResult.OK"/> once the
    /// password was changed; the profile then says so.
    /// </summary>
    public sealed class SifreDegistirmeForm : AppDialog
    {
        private readonly IAuthService _auth;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();

        private readonly TextBox txtMevcutSifre = new() { Name = nameof(txtMevcutSifre), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtYeniSifre = new() { Name = nameof(txtYeniSifre), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtYeniSifreTekrar = new() { Name = nameof(txtYeniSifreTekrar), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnSifreDegistir = FormLayout.Button(nameof(btnSifreDegistir), "Şifreyi değiştir", ButtonKind.Primary);

        public SifreDegistirmeForm(IAuthService auth, UserSession session)
            : base("Şifre Değiştir", "Hesabınızın şifresini yenileyin.", Glyphs.Key, new Size(560, 420), new Size(460, 380), modal: true)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(session);

            _auth = auth;
            _session = session;
            BuildLayout();
            _change.HoldClosingOf(this);
            btnSifreDegistir.Click += async (_, _) => await ChangeAndCloseAsync();
        }

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

        private async Task ChangeAndCloseAsync()
        {
            if (await ChangeAsync() && !IsDisposed)
            {
                txtYeniSifre.Clear();
                txtYeniSifreTekrar.Clear();
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(SifreDegistirmeForm);
            var current = FormLayout.Field("&Mevcut şifre", FormLayout.Input(txtMevcutSifre));
            current.Margin = new Padding(0, 0, 0, Spacing.M);
            var passwords = FormLayout.FieldRow(
                "passwordRow",
                (FormLayout.Field("&Yeni şifre", FormLayout.Input(txtYeniSifre), string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength} karakter.")), 50),
                (FormLayout.Field("Yeni şifre &tekrar", FormLayout.Input(txtYeniSifreTekrar)), 50));
            var card = FormLayout.Card("passwordCard", null, FormLayout.Stack("passwordForm", current, passwords), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            AcceptButton = btnSifreDegistir;
            SetFooterNote("Yeni şifre bir sonraki girişte geçerli olur.");
            SetFooterButtons(btnSifreDegistir, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
