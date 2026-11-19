using System.Globalization;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The admin's account management. Passwords are never shown: the password field sets a new one and
    /// may stay empty when an account is changed.
    /// </summary>
    public partial class KullaniciYonetimPanel : Form
    {
        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();

        public KullaniciYonetimPanel(IAccountService accounts, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            cmbRol.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRol.ReplaceItems(RoleChoice.All);
            cmbRol.SelectedIndex = -1;
            txtKullaniciAdi.MaxLength = InputRules.StoredUserNameMaxLength;
            txtSifre.MaxLength = PasswordPolicy.MaxLength;
            txtSifre.UseSystemPasswordChar = true;
            txtEposta.MaxLength = InputRules.EmailMaxLength;
            txtTelefon.MaxLength = 12;
            txtTelefon.FormatAsPhone();
            dataGridView1.UseReadOnlyList();
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        internal int? SelectedUserId { get; private set; }

        internal IReadOnlyList<UserSummary> ShownUsers { get; private set; } = [];

        internal async Task LoadUsersAsync()
        {
            var request = _loads.Next();
            var users = await _accounts.GetAllAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownUsers = users;
            dataGridView1.DataSource = users.Select(u => new UserRow(u)).ToList();
            dataGridView1.ClearSelection();
        }

        internal void SelectUser(UserSummary user)
        {
            ArgumentNullException.ThrowIfNull(user);

            SelectedUserId = user.Id;
            txtID.Text = user.Id.ToString(CultureInfo.InvariantCulture);
            txtKullaniciAdi.Text = user.UserName;
            txtSifre.Clear();
            txtEposta.Text = user.Email ?? string.Empty;
            txtTelefon.Text = user.Phone ?? string.Empty;
            cmbRol.SelectedItem = RoleChoice.All.First(r => r.Role == user.Role);
        }

        internal Task<bool> AddAsync() =>
            WriteAsync(async () =>
            {
                await _accounts.AddAsync(_session, ReadInput());
                Prompts.Inform("Kullanıcı başarıyla eklendi.");
                ClearForm();
                await LoadUsersAsync();
            });

        internal Task<bool> UpdateAsync()
        {
            if (SelectedUserId is not { } id)
            {
                Prompts.Warn("Lütfen güncellemek için bir kullanıcı seçin.");
                return Task.FromResult(false);
            }

            return WriteAsync(async () =>
            {
                await _accounts.UpdateAsync(_session, id, ReadInput());
                Prompts.Inform("Kullanıcı başarıyla güncellendi.");
                ClearForm();
                await LoadUsersAsync();
            });
        }

        internal Task<bool> DeleteAsync()
        {
            if (SelectedUserId is not { } id)
            {
                Prompts.Warn("Lütfen silinecek bir kullanıcı seçin.");
                return Task.FromResult(false);
            }

            return WriteAsync(async () =>
            {
                if (!Prompts.Confirm("Bu kullanıcıyı silmek istediğinizden emin misiniz?"))
                {
                    return;
                }

                await _accounts.DeleteAsync(_session, id);
                Prompts.Inform("Kullanıcı başarıyla silindi.");
                ClearForm();
                await LoadUsersAsync();
            });
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadUsersAsync);
        }

        private Task<bool> WriteAsync(Func<Task> write) =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    try
                    {
                        await write();
                    }
                    finally
                    {
                        txtSifre.Clear();
                    }
                },
                btnKullaniciEkle,
                btnGuncelle,
                btnKullaniciSil);

        private AccountInput ReadInput() => new(
            txtKullaniciAdi.Text,
            txtSifre.Text,
            txtEposta.Text,
            txtTelefon.Text,
            (cmbRol.SelectedItem as RoleChoice)?.Role ?? (UserRole)(-1));

        private void ClearForm()
        {
            SelectedUserId = null;
            dataGridView1.ClearSelection();
            txtID.Clear();
            txtKullaniciAdi.Clear();
            txtSifre.Clear();
            txtEposta.Clear();
            txtTelefon.Clear();
            cmbRol.SelectedIndex = -1;
        }

        private void dataGridView1_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is UserRow row)
            {
                SelectUser(row.User);
            }
        }

        private void btnPanelCikis_Click(object? sender, EventArgs e) => Close();

        private async void btnGuncelle_Click(object? sender, EventArgs e) => await UpdateAsync();

        private async void btnKullaniciSil_Click(object? sender, EventArgs e) => await DeleteAsync();

        private async void btnKullaniciEkle_Click(object? sender, EventArgs e) => await AddAsync();
    }
}
