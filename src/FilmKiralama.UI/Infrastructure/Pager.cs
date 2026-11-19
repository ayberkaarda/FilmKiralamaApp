using System.Globalization;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// "Önceki" and "Sonraki" with "Sayfa 2 / 5 (37 film)" between them, in one row. Showing a page is not a user
/// choice and raises nothing; only a click asks for another page.
/// </summary>
internal sealed class Pager : TableLayoutPanel
{
    private readonly ThemedButton _previous = FormLayout.Button("btnOncekiSayfa", "Önceki", ButtonKind.Secondary, Glyphs.ChevronLeft);
    private readonly ThemedButton _next = FormLayout.Button("btnSonrakiSayfa", "Sonraki", ButtonKind.Secondary, Glyphs.ChevronRight);
    private readonly Label _label = FormLayout.Text("lblSayfa");
    private int _page = 1;
    private int _pageCount = 1;

    public Pager()
    {
        Name = "pnlSayfa";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        ColumnCount = 3;
        RowCount = 1;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < 3; i++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        _next.GlyphAfterText = true;
        _previous.Enabled = false;
        _next.Enabled = false;
        _previous.Margin = new Padding(0, 0, Spacing.S, 0);
        _next.Margin = new Padding(Spacing.S, 0, 0, 0);
        _label.ForeColor = Theme.Current.TextSecondary;
        _label.Anchor = AnchorStyles.None;
        _label.Margin = new Padding(Spacing.S, 0, Spacing.S, 0);
        Controls.Add(_previous, 0, 0);
        Controls.Add(_label, 1, 0);
        Controls.Add(_next, 2, 0);
        _previous.Click += (_, _) => PageRequested?.Invoke(this, _page - 1);
        _next.Click += (_, _) => PageRequested?.Invoke(this, _page + 1);
    }

    /// <summary>Raised with the page number when the user asks for the previous or the next page.</summary>
    public event EventHandler<int>? PageRequested;

    public int Page => _page;

    public string Summary => _label.Text;

    public Button Previous => _previous;

    public Button Next => _next;

    /// <summary>Shows the current page of <paramref name="pageCount"/> and the number of rows.</summary>
    public void Show(int page, int pageCount, int totalCount, string noun)
    {
        _page = page;
        _pageCount = Math.Max(1, pageCount);
        _label.Text = string.Create(CultureInfo.InvariantCulture, $"Sayfa {page} / {_pageCount} ({totalCount} {noun})");
        _previous.Enabled = _page > 1;
        _next.Enabled = _page < _pageCount;
    }
}
