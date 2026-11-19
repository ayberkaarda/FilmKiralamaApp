using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's rating and comment of a film they returned: stored, changed or deleted. Closes with
    /// <see cref="DialogResult.OK"/> when something was saved or deleted.
    /// </summary>
    public sealed class DegerlendirmeForm : Form
    {
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly ReviewableFilm _film;
        private readonly ExclusiveOperation _write = new();
        private readonly ComboBox _rating;
        private readonly TextBox _comment;
        private readonly Label _remaining;
        private readonly Label _hiddenNotice;
        private readonly Button _save;
        private readonly Button _delete;
        private int? _reviewId;

        public DegerlendirmeForm(IReviewService reviews, UserSession session, ReviewableFilm film)
        {
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(film);

            _reviews = reviews;
            _session = session;
            _film = film;
            Prompts = new MessageBoxPrompts(this);

            Text = "Film Değerlendir";
            Icon = AppAssets.Icon;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(480, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var title = new Label
            {
                Name = "lblFilm",
                Text = film.Title + " (" + film.Year.ToString(CultureInfo.InvariantCulture) + ")",
                Location = new Point(20, 16),
                Size = new Size(440, 28),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                UseMnemonic = false,
                AutoEllipsis = true,
            };
            var ratingCaption = new Label { Text = "Puanınız", Location = new Point(20, 58), AutoSize = true };
            _rating = new ComboBox
            {
                Name = "cmbPuan",
                Location = new Point(20, 82),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _rating.Items.AddRange([.. ReviewDisplay.Choices]);

            var commentCaption = new Label { Text = "Yorumunuz (isteğe bağlı)", Location = new Point(20, 122), AutoSize = true };
            _comment = new TextBox
            {
                Name = "txtYorum",
                Location = new Point(20, 146),
                Size = new Size(440, 110),
                Multiline = true,
                AcceptsReturn = false,
                ScrollBars = ScrollBars.Vertical,
                MaxLength = ReviewPolicy.MaxCommentLength,
            };
            _remaining = new Label { Name = "lblKalan", Location = new Point(20, 260), Size = new Size(220, 22) };
            _hiddenNotice = new Label
            {
                Name = "lblGizli",
                Text = "Bu yorum yönetici tarafından gizlendi; diğer kullanıcılar görmüyor.",
                Location = new Point(20, 284),
                Size = new Size(440, 22),
                Visible = false,
            };
            _save = new Button { Name = "btnKaydet", Text = "Kaydet", Location = new Point(250, 312), Size = new Size(100, 34) };
            _delete = new Button { Name = "btnSil", Text = "Sil", Location = new Point(360, 312), Size = new Size(100, 34), Enabled = false };
            _comment.TextChanged += (_, _) => ShowRemaining();
            _save.Click += async (_, _) => await SaveAsync();
            _delete.Click += async (_, _) => await DeleteAsync();

            Controls.AddRange([title, ratingCaption, _rating, commentCaption, _comment, _remaining, _hiddenNotice, _save, _delete]);
            AcceptButton = _save;
            ShowRemaining();
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Whether the notice that an admin hid the review is shown.</summary>
        internal bool ShowsHiddenNotice { get; private set; }

        /// <summary>Fills the form with the account's own review, if there is one.</summary>
        internal async Task LoadAsync()
        {
            var own = await _reviews.GetOwnAsync(_session, _film.FilmId);
            _reviewId = own?.Id;
            _rating.SelectedItem = ReviewDisplay.Choices.FirstOrDefault(c => c.Rating == own?.Rating);
            _comment.Text = own?.Comment ?? string.Empty;
            ShowsHiddenNotice = own?.IsHidden == true;
            _hiddenNotice.Visible = ShowsHiddenNotice;
            _delete.Enabled = own is not null;
        }

        /// <summary>Saves the rating and the comment; ignored while a save or a delete is running.</summary>
        internal async Task<bool> SaveAsync()
        {
            var saved = false;
            await this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (_rating.SelectedItem is not RatingChoice choice)
                    {
                        Prompts.Warn("Lütfen 1 ile 5 arasında bir puan seçin.");
                        _write.FocusAfterUnlock(_rating);
                        return;
                    }

                    await _reviews.SaveAsync(_session, _film.FilmId, new ReviewInput(choice.Rating, _comment.Text));
                    saved = true;
                    Prompts.Inform("Değerlendirmeniz kaydedildi.");
                },
                _save,
                _delete);
            if (saved)
            {
                DialogResult = DialogResult.OK;
            }

            return saved;
        }

        /// <summary>Deletes the account's review after a confirmation; ignored while a save or a delete is running.</summary>
        internal async Task<bool> DeleteAsync()
        {
            var deleted = false;
            await this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (_reviewId is not { } id || !Prompts.Confirm("Değerlendirmenizi silmek istediğinize emin misiniz?"))
                    {
                        return;
                    }

                    await _reviews.DeleteOwnAsync(_session, id);
                    deleted = true;
                    Prompts.Inform("Değerlendirmeniz silindi.");
                },
                _save,
                _delete);
            if (deleted)
            {
                DialogResult = DialogResult.OK;
            }

            return deleted;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadAsync);
        }

        private void ShowRemaining() =>
            _remaining.Text = (ReviewPolicy.MaxCommentLength - _comment.TextLength).ToString(CultureInfo.InvariantCulture) + " karakter kaldı";
    }
}
