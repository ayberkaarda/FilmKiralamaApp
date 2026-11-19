using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI
{
    /// <summary>The admin's review moderation: every review, newest first, page by page; hide or show one.</summary>
    public sealed class YorumYonetimForm : Form
    {
        private readonly IReviewService _reviews;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly DataGridView _grid;
        private readonly CheckBox _hiddenOnly;
        private readonly Pager _pager;
        private readonly Button _hide;
        private readonly Button _show;
        private bool _binding;

        public YorumYonetimForm(IReviewService reviews, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(reviews);
            ArgumentNullException.ThrowIfNull(session);

            _reviews = reviews;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            Text = "Yorum Yönetimi";
            Icon = AppAssets.Icon;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(1100, 640);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _hiddenOnly = new CheckBox { Name = "chkGizliler", Text = "Yalnızca gizlenen yorumlar", Location = new Point(16, 16), AutoSize = true };
            _hiddenOnly.CheckedChanged += async (_, _) => await this.RunAsync(() => LoadAsync(1));

            _grid = new DataGridView
            {
                Name = "dgvYorumlar",
                Location = new Point(16, 50),
                Size = new Size(1068, 500),
                AutoGenerateColumns = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            _grid.UseReadOnlyList();
            _grid.SelectionChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user.
                if (!_binding)
                {
                    UpdateButtons();
                }
            };

            _pager = new Pager(this, new Point(16, 560));
            _pager.PageRequested += async (_, page) => await this.RunAsync(() => LoadAsync(page));

            _hide = new Button { Name = "btnGizle", Text = "Gizle", Location = new Point(764, 590), Size = new Size(150, 36), Enabled = false };
            _show = new Button { Name = "btnGoster", Text = "Göster", Location = new Point(934, 590), Size = new Size(150, 36), Enabled = false };
            _hide.Click += async (_, _) => await SetHiddenAsync(true);
            _show.Click += async (_, _) => await SetHiddenAsync(false);

            Controls.AddRange([_hiddenOnly, _grid, _hide, _show]);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The reviews shown now.</summary>
        internal PagedResult<ReviewDetails>? ShownReviews { get; private set; }

        /// <summary>The review whose row is selected, or <see langword="null"/>.</summary>
        internal ReviewDetails? Selected => _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].DataBoundItem is ReviewRow row ? row.Review : null;

        /// <summary>Loads one page of the reviews; only the latest request is shown and binding selects nothing.</summary>
        internal async Task LoadAsync(int page)
        {
            var request = _loads.Next();
            var result = await _reviews.GetAllAsync(_session, _hiddenOnly.Checked, page);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownReviews = result;
            _binding = true;
            try
            {
                _grid.DataSource = result.Items.Select(r => new ReviewRow(r)).ToList();
                _grid.ClearSelection();
            }
            finally
            {
                _binding = false;
            }

            _pager.Show(result.Page, result.PageCount, result.TotalCount, "yorum");
            UpdateButtons();
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
                _hide,
                _show);

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(() => LoadAsync(1));
        }

        private void UpdateButtons()
        {
            var selected = Selected;
            _hide.Enabled = selected is { IsHidden: false };
            _show.Enabled = selected is { IsHidden: true };
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
