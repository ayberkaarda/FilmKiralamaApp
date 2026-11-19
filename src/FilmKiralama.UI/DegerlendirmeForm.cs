using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>
    /// The customer's rating and comment of a film they returned: stored, changed or deleted. Closes with
    /// <see cref="DialogResult.OK"/> and <see cref="ResultMessage"/> set when something was saved or deleted.
    /// </summary>
    public sealed class DegerlendirmeForm : AppDialog
    {
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly ReviewableFilm _film;
        private readonly ExclusiveOperation _write = new();
        private int? _reviewId;

        private readonly ThemedComboBox cmbPuan = FormLayout.Picker(nameof(cmbPuan), 220, "Puan seçin");
        private readonly TextBox txtYorum = new() { Name = nameof(txtYorum), Multiline = true, AcceptsReturn = false, ScrollBars = ScrollBars.Vertical, MaxLength = ReviewPolicy.MaxCommentLength };
        private readonly Label lblKalan = FormLayout.Hint(string.Empty);
        private readonly Label lblGizli = FormLayout.Text(nameof(lblGizli), "Bu yorum yönetici tarafından gizlendi; diğer kullanıcılar görmüyor. Değiştirdiğinizde de gizli kalır.");
        private readonly ThemedButton btnKaydet = FormLayout.Button(nameof(btnKaydet), "Kaydet", ButtonKind.Primary, Glyphs.Save);
        private readonly ThemedButton btnSil = FormLayout.Button(nameof(btnSil), "Sil", ButtonKind.Danger, Glyphs.Delete);
        private CardPanel _hiddenNote = null!;

        public DegerlendirmeForm(IReviewService reviews, UserSession session, ReviewableFilm film)
            : base("Film Değerlendir", Describe(film), Glyphs.Star, new Size(580, 500), new Size(460, 440), modal: true)
        {
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(film);

            _reviews = reviews;
            _session = session;
            _film = film;
            BuildLayout();
            _write.HoldClosingOf(this);

            btnSil.Enabled = false;
            txtYorum.TextChanged += (_, _) => ShowRemaining();
            btnKaydet.Click += async (_, _) => await SaveAndCloseAsync();
            btnSil.Click += async (_, _) => await DeleteAndCloseAsync();
            ShowRemaining();
        }

        /// <summary>Whether the notice that an admin hid the review is shown.</summary>
        internal bool ShowsHiddenNotice { get; private set; }

        /// <summary>What the screen that opened this one says once the review was saved or deleted.</summary>
        internal string? ResultMessage { get; private set; }

        /// <summary>Fills the form with the account's own review, if there is one.</summary>
        internal async Task LoadAsync()
        {
            var own = await _reviews.GetOwnAsync(_session, _film.FilmId);
            _reviewId = own?.Id;
            cmbPuan.SelectedItem = ReviewDisplay.Choices.FirstOrDefault(c => c.Rating == own?.Rating);
            txtYorum.Text = own?.Comment ?? string.Empty;
            ShowsHiddenNotice = own?.IsHidden == true;
            FormLayout.SetShown(_hiddenNote, ShowsHiddenNotice);
            btnSil.Enabled = own is not null;
        }

        /// <summary>Saves the rating and the comment; ignored while a save or a delete is running.</summary>
        internal async Task<bool> SaveAsync()
        {
            var saved = false;
            await this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (cmbPuan.SelectedItem is not RatingChoice choice)
                    {
                        Prompts.Warn("Lütfen 1 ile 5 arasında bir puan seçin.");
                        _write.FocusAfterUnlock(cmbPuan);
                        return;
                    }

                    await _reviews.SaveAsync(_session, _film.FilmId, new ReviewInput(choice.Rating, txtYorum.Text));
                    saved = true;
                    ResultMessage = "\"" + _film.Title + "\" için değerlendirmeniz kaydedildi.";
                },
                btnKaydet,
                btnSil);
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
                    ResultMessage = "\"" + _film.Title + "\" için değerlendirmeniz silindi.";
                },
                btnKaydet,
                btnSil);
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

        private static string Describe(ReviewableFilm? film) =>
            film is null ? string.Empty : film.Title + " · " + film.Genre + ", " + film.Year.ToString(CultureInfo.InvariantCulture);

        private async Task SaveAndCloseAsync()
        {
            if (await SaveAsync() && !IsDisposed)
            {
                Close();
            }
        }

        private async Task DeleteAndCloseAsync()
        {
            if (await DeleteAsync() && !IsDisposed)
            {
                Close();
            }
        }

        private void ShowRemaining() =>
            lblKalan.Text = (ReviewPolicy.MaxCommentLength - txtYorum.TextLength).ToString(CultureInfo.InvariantCulture) + " karakter kaldı";

        private void BuildLayout()
        {
            Name = nameof(DegerlendirmeForm);
            cmbPuan.Items.AddRange([.. ReviewDisplay.Choices]);
            _hiddenNote = FormLayout.Note("hiddenNote", Glyphs.Hide, lblGizli);

            var rating = FormLayout.Field("&Puanınız", cmbPuan);
            rating.Margin = new Padding(0, 0, 0, Spacing.M);
            cmbPuan.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            lblKalan.Name = nameof(lblKalan);
            var comment = FormLayout.Field("&Yorumunuz (isteğe bağlı)", FormLayout.Input(txtYorum, Sizes.NoteHeight + Spacing.XL));
            comment.Controls.Add(lblKalan);
            comment.Margin = Padding.Empty;
            var plain = FormLayout.Hint("Yorumlar düz metin olarak gösterilir; satır sonu kullanılamaz. Adınız diğer kullanıcılara kısaltılarak görünür.");
            plain.Margin = new Padding(0, Spacing.M, 0, 0);
            var card = FormLayout.Card("reviewCard", null, FormLayout.Stack("reviewForm", rating, comment, plain), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("reviewPage", (_hiddenNote, false), (card, false)));
            FormLayout.SetShown(_hiddenNote, false);

            AcceptButton = btnKaydet;
            SetFooterNote("Yalnızca kiralayıp iade ettiğiniz filmleri puanlayabilirsiniz.");
            SetFooterButtons(btnSil, btnKaydet, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
