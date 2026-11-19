using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI
{
    /// <summary>The admin's review moderation: every review, newest first, page by page; hide or show one.</summary>
    public sealed class YorumYonetimForm : AppDialog
    {
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly Pager _pager = new();
        private bool _binding;

        private readonly ThemedCheckBox chkGizliler = new() { Name = nameof(chkGizliler), Text = "Yalnızca &gizlenen yorumlar" };
        private readonly ThemedGrid dgvYorumlar = new() { Name = nameof(dgvYorumlar) };
        private readonly ThemedButton btnGizle = FormLayout.Button(nameof(btnGizle), "Gizle", ButtonKind.Secondary, Glyphs.Hide);
        private readonly ThemedButton btnGoster = FormLayout.Button(nameof(btnGoster), "Göster", ButtonKind.Secondary, Glyphs.View);

        public YorumYonetimForm(IReviewService reviews, UserSession session)
            : base("Yorum Yönetimi", "Uygunsuz yorumları gizleyin ya da yeniden gösterin; gizli yorum ortalamaya katılmaz.", Glyphs.Comment, new Size(1100, 660), new Size(860, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(session);

            _reviews = reviews;
            _session = session;
            BuildLayout();
            _write.HoldClosingOf(this);

            btnGizle.Enabled = false;
            btnGoster.Enabled = false;
            chkGizliler.CheckedChanged += async (_, _) => await this.RunAsync(() => LoadAsync(1));
            _pager.PageRequested += async (_, page) => await this.RunAsync(() => LoadAsync(page));
            dgvYorumlar.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };
            btnGizle.Click += async (_, _) => await SetHiddenAsync(true);
            btnGoster.Click += async (_, _) => await SetHiddenAsync(false);
        }

        /// <summary>The reviews shown now.</summary>
        internal PagedResult<ReviewDetails>? ShownReviews { get; private set; }

        /// <summary>The review whose row is selected, or <see langword="null"/>.</summary>
        internal ReviewDetails? Selected => dgvYorumlar.SelectedRows.Count == 1 && dgvYorumlar.SelectedRows[0].DataBoundItem is ReviewRow row ? row.Review : null;

        /// <summary>Loads one page of the reviews; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync(int page)
        {
            var request = _loads.Next();
            dgvYorumlar.IsLoading = true;
            try
            {
                var result = await _reviews.GetAllAsync(_session, chkGizliler.Checked, page);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownReviews = result;
                _binding = true;
                try
                {
                    dgvYorumlar.DataSource = result.Items.Select(r => new ReviewRow(r)).ToList();
                    dgvYorumlar.ClearSelection();
                }
                finally
                {
                    _binding = false;
                }

                _pager.Show(result.Page, result.PageCount, result.TotalCount, "yorum");
                UpdateButtons();
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    dgvYorumlar.IsLoading = false;
                }
            }
        }

        /// <summary>Hides or shows the selected review; ignored while another change is running.</summary>
        internal Task<bool> SetHiddenAsync(bool hidden) =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    if (Selected is not { } review)
                    {
                        Prompts.Warn("Lütfen bir yorum seçin.");
                        return;
                    }

                    await _reviews.SetHiddenAsync(_session, review.Id, hidden);
                    Prompts.Inform(hidden ? "Yorum gizlendi." : "Yorum yeniden gösteriliyor.");
                    await LoadAsync(ShownReviews?.Page ?? 1);
                },
                btnGizle,
                btnGoster);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(() => LoadAsync(1));
        }

        private void UpdateButtons()
        {
            var selected = Selected;
            btnGizle.Enabled = selected is { IsHidden: false };
            btnGoster.Enabled = selected is { IsHidden: true };
        }

        private void BuildLayout()
        {
            Name = nameof(YorumYonetimForm);
            var p = Theme.Current;
            var top = FormLayout.Bar("filterBar", (chkGizliler, false), (FormLayout.Spacer(), true), (_pager, false));
            top.Margin = new Padding(0, 0, 0, Spacing.M);

            dgvYorumlar.EmptyText = "Gösterilecek yorum yok.";
            dgvYorumlar.FitColumns = new HashSet<string> { nameof(ReviewRow.Id), nameof(ReviewRow.Rating), nameof(ReviewRow.State) };
            dgvYorumlar.ColumnWeights = new Dictionary<string, float> { [nameof(ReviewRow.Film)] = 120, [nameof(ReviewRow.UserName)] = 100, [nameof(ReviewRow.Comment)] = 260 };
            dgvYorumlar.CellTone = (column, value) => column == nameof(ReviewRow.State) && value is "Gizli" ? p.Warning : null;

            // A comment wraps over as many lines as it needs, so the admin reads it whole before hiding it.
            dgvYorumlar.DataBindingComplete += (_, _) =>
            {
                if (dgvYorumlar.Columns[nameof(ReviewRow.Comment)] is { } comment)
                {
                    comment.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                }
            };
            var card = FormLayout.Card("reviewsCard", null, dgvYorumlar);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("reviewsPage", (top, false), (card, true)));

            SetFooterNote("Gizlenen yorum, yazarı değiştirse ya da silip yeniden yazsa da gizli kalır.");
            SetFooterButtons(btnGizle, btnGoster, CreateCloseButton("btnKapat"));
            FinishLayout();
        }

        /// <summary>A review row; the comment is plain text in a cell, never markup.</summary>
        private sealed class ReviewRow(ReviewDetails review)
        {
            [Browsable(false)]
            public ReviewDetails Review { get; } = review;

            [DisplayName("No")]
            public int Id => Review.Id;

            [DisplayName("Film")]
            public string Film => Review.FilmTitle;

            [DisplayName("Kullanıcı")]
            public string UserName => Review.UserName;

            [DisplayName("Puan")]
            public string Rating => Review.Rating.ToString(CultureInfo.InvariantCulture) + " " + ReviewDisplay.Stars(Review.Rating);

            [DisplayName("Yorum")]
            public string Comment => Review.Comment ?? string.Empty;

            [DisplayName("Durum")]
            public string State => Review.IsHidden ? "Gizli" : "Görünür";
        }
    }
}
