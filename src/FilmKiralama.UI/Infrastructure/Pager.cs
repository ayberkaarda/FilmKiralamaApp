using System.Globalization;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// "Önceki" and "Sonraki" buttons with "Sayfa 2 / 5 (37 film)" between them. Showing a page is not a user
/// choice and raises nothing; only a click asks for another page.
/// </summary>
internal sealed class Pager : Panel
{
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Label _label;
    private int _page = 1;
    private int _pageCount = 1;

    public Pager(Control parent, Point location)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Name = "pnlSayfa";
        Location = location;
        Size = new Size(500, 32);
        BackColor = Color.Transparent;
        _previous = new Button { Name = "btnOncekiSayfa", Text = "◀ Önceki", Location = new Point(0, 0), Size = new Size(110, 32), Enabled = false };
        _label = new Label
        {
            Name = "lblSayfa",
            Location = new Point(120, 4),
            Size = new Size(260, 24),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
        };
        _next = new Button { Name = "btnSonrakiSayfa", Text = "Sonraki ▶", Location = new Point(390, 0), Size = new Size(110, 32), Enabled = false };
        _previous.Click += (_, _) => PageRequested?.Invoke(this, _page - 1);
        _next.Click += (_, _) => PageRequested?.Invoke(this, _page + 1);
        Controls.AddRange([_previous, _label, _next]);
        parent.Controls.Add(this);
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
