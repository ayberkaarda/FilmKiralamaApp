using System.Globalization;
using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The admin's account management. Passwords are never shown: the password field sets a new one and
    /// may stay empty when an account is changed.
    /// </summary>
    public sealed class KullaniciYonetimPanel : AppDialog
    {
        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        private readonly ThemedGrid dataGridView1 = new() { Name = nameof(dataGridView1) };
        private readonly TextBox txtID = new() { Name = nameof(txtID), ReadOnly = true, TabStop = false };
        private readonly TextBox txtKullaniciAdi = new() { Name = nameof(txtKullaniciAdi), MaxLength = InputRules.StoredUserNameMaxLength };
        private readonly TextBox txtSifre = new() { Name = nameof(txtSifre), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtEposta = new() { Name = nameof(txtEposta), MaxLength = InputRules.EmailMaxLength };
        private readonly TextBox txtTelefon = new() { Name = nameof(txtTelefon), MaxLength = 12 };
        private readonly ThemedComboBox cmbRol = FormLayout.Picker(nameof(cmbRol), 140, "Rol seçin");
        private readonly ThemedButton btnKullaniciEkle = FormLayout.Button(nameof(btnKullaniciEkle), "Hesap ekle", ButtonKind.Primary, Glyphs.AddPerson);
        private readonly ThemedButton btnGuncelle = FormLayout.Button(nameof(btnGuncelle), "Güncelle", ButtonKind.Secondary, Glyphs.Save);
        private readonly ThemedButton btnKullaniciSil = FormLayout.Button(nameof(btnKullaniciSil), "Sil", ButtonKind.Danger, Glyphs.Delete);
        private readonly ThemedButton btnTemizle = FormLayout.Button(nameof(btnTemizle), "Formu temizle", ButtonKind.Subtle, Glyphs.Cancel);

        public KullaniciYonetimPanel(IAccountService accounts, UserSession session)
            : base("Kullanıcı Yönetimi", "Hesapları ekleyin, rollerini ve iletişim bilgilerini değiştirin.", Glyphs.People, new Size(1120, 640), new Size(900, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            cmbRol.ReplaceItems(RoleChoice.All);
            cmbRol.SelectedIndex = -1;
            txtTelefon.FormatAsPhone();
            BuildLayout();
            _write.HoldClosingOf(this);

            dataGridView1.CellClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is UserRow row)
                {
                    SelectUser(row.User);
                }
            };
            dataGridView1.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };
            btnKullaniciEkle.Click += async (_, _) => await AddAsync();
            btnGuncelle.Click += async (_, _) => await UpdateAsync();
            btnKullaniciSil.Click += async (_, _) => await DeleteAsync();
            btnTemizle.Click += (_, _) => ClearForm();
            UpdateButtons();
        }

        internal int? SelectedUserId { get; private set; }

        internal IReadOnlyList<UserSummary> ShownUsers { get; private set; } = [];

        internal async Task LoadUsersAsync()
        {
            var request = _loads.Next();
            dataGridView1.IsLoading = true;
            try
            {
                var users = await _accounts.GetAllAsync(_session);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownUsers = users;
                _binding = true;
                try
                {
                    dataGridView1.DataSource = users.Select(u => new UserRow(u)).ToList();
                    dataGridView1.ClearSelection();
                }
                finally
                {
                    _binding = false;
                }

                UpdateButtons();
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dataGridView1.IsLoading = false;
                }
            }
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
            UpdateButtons();
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
                btnKullaniciSil,
                btnTemizle);

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
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var selected = SelectedUserId is not null;
            btnGuncelle.Enabled = selected;
            btnKullaniciSil.Enabled = selected;
        }

        private void BuildLayout()
        {
            Name = nameof(KullaniciYonetimPanel);
            var p = Theme.Current;
            dataGridView1.EmptyText = "Henüz hesap yok.";
            dataGridView1.ColumnHeaders = new Dictionary<string, string>
            {
                [nameof(UserRow.Id)] = "No",
                [nameof(UserRow.UserName)] = "Kullanıcı adı",
                [nameof(UserRow.Email)] = "E-posta",
                [nameof(UserRow.Phone)] = "Telefon",
            };
            dataGridView1.FitColumns = new HashSet<string> { nameof(UserRow.Id), nameof(UserRow.Phone), nameof(UserRow.Role) };
            dataGridView1.ColumnWeights = new Dictionary<string, float> { [nameof(UserRow.Email)] = 130 };
            dataGridView1.CellTone = (column, value) => column == nameof(UserRow.Role) && value is "Admin" ? p.AccentText : null;
            var listCard = FormLayout.Card("accountsCard", "Hesaplar", dataGridView1);
            listCard.Margin = Padding.Empty;

            var name = FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtKullaniciAdi));
            var password = FormLayout.Field(
                "&Yeni şifre",
                FormLayout.Input(txtSifre),
                string.Create(CultureInfo.CurrentCulture, $"Yeni hesapta zorunlu; değiştirirken boş bırakılırsa eski şifre kalır. En az {PasswordPolicy.MinLength} karakter."));
            var email = FormLayout.Field("&E-posta", FormLayout.Input(txtEposta));
            var phoneRole = FormLayout.FieldRow(
                "phoneRoleRow",
                (FormLayout.Field("&Telefon", FormLayout.Input(txtTelefon)), 55),
                (FormLayout.Field("&Rol", cmbRol), 45));
            var id = FormLayout.Field("Hesap numarası", FormLayout.Input(txtID));
            name.Margin = password.Margin = email.Margin = new Padding(0, 0, 0, Spacing.M);
            id.Margin = new Padding(0, 0, 0, Spacing.M);
            var actions = FormLayout.Bar("editorActions", (btnKullaniciEkle, false), (btnGuncelle, false), (FormLayout.Spacer(), true), (btnKullaniciSil, false));
            btnKullaniciEkle.Margin = new Padding(0, 0, Spacing.S, 0);
            btnGuncelle.Margin = Padding.Empty;
            btnKullaniciSil.Margin = Padding.Empty;
            actions.Margin = new Padding(0, Spacing.S, 0, 0);
            btnTemizle.Margin = new Padding(0, Spacing.S, 0, 0);
            var editor = FormLayout.Stack("accountForm", id, name, password, email, phoneRole, actions, btnTemizle);
            btnTemizle.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var editorCard = FormLayout.Card("accountCard", "Hesap bilgileri", editor, fill: false);
            editorCard.Margin = Padding.Empty;
            var right = FormLayout.Page("editorColumn", (editorCard, false));

            Content.Controls.Add(FormLayout.Columns("layout", listCard, 58, right));
            SetFooterNote("Yöneticiler ve kiralaması olan hesaplar silinemez; son yönetici yönetici olarak kalır.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
