using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>A card of a <see cref="CardGrid"/>: it lays out its own content and tells the grid how tall it is at a width.</summary>
internal interface IGridCard
{
    /// <summary>The height the card needs at <paramref name="width"/> device pixels.</summary>
    int MeasureHeight(int width);
}

/// <summary>
/// Cards in rows of equal width that wrap to the width of the grid and scroll vertically inside it, so a page of
/// films does not make the window taller than the screen. The cards of a row share the row's height. While the
/// grid has no cards it shows a message: loading, or why it is empty.
/// </summary>
internal sealed class CardGrid : Panel, IScrollsOwnContent
{
    private string _emptyText = string.Empty;
    private bool _loading;
    private bool _arranging;

    public CardGrid()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = Theme.Current.Page;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Width of a card in logical pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ItemWidth { get; set; } = Sizes.PosterCardWidth;

    /// <summary>Space between cards in logical pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Gap { get; set; } = Spacing.L;

    /// <summary>Shown in the middle of the grid while it has no cards.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            Invalidate();
        }
    }

    /// <summary>Shown instead of <see cref="EmptyText"/> while <see cref="IsLoading"/> is set.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LoadingText { get; set; } = "Yükleniyor…";

    /// <summary>A request is on its way; an empty grid says so instead of "nothing here".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsLoading
    {
        get => _loading;
        set
        {
            if (_loading != value)
            {
                _loading = value;
                Invalidate();
            }
        }
    }

    /// <summary>The message the grid shows now instead of cards, or an empty text.</summary>
    internal string ShownMessage => Controls.Count > 0 ? string.Empty : _loading ? LoadingText : _emptyText;

    /// <summary>Replaces the cards; the previous ones are disposed. The view returns to the top.</summary>
    public void SetItems(IEnumerable<Control> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        SuspendLayout();
        try
        {
            foreach (var old in Controls.Cast<Control>().ToList())
            {
                old.Dispose();
            }

            AutoScrollPosition = Point.Empty;
            foreach (var card in cards)
            {
                Controls.Add(card);
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // The cards are placed first: the scroll range the base layout works out follows where they are.
        Arrange();
        base.OnLayout(levent);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        Arrange();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        PaintMessage(e.Graphics);
    }

    /// <summary>
    /// Paints the empty or loading message into <paramref name="graphics"/>; also the entry point of the offscreen
    /// screenshot tool.
    /// </summary>
    internal void PaintMessage(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var message = ShownMessage;
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var pad = Theme.Px(this, Spacing.XL);
        var area = new Rectangle(pad, pad, Math.Max(0, ClientSize.Width - (2 * pad)), Math.Max(0, ClientSize.Height - (2 * pad)));
        TextRenderer.DrawText(
            graphics,
            message,
            Theme.Fonts.Body,
            area,
            Theme.Current.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    /// <summary>Places the cards row by row and sets the scroll range to their height.</summary>
    private void Arrange()
    {
        if (_arranging)
        {
            return;
        }

        _arranging = true;
        try
        {
            var cards = Controls.Cast<Control>().ToList();
            var gap = Theme.Px(this, Gap);
            var inner = Math.Max(1, ClientSize.Width);
            var wanted = Theme.Px(this, ItemWidth);
            var columns = Math.Max(1, (inner + gap) / (wanted + gap));

            // The cards share the whole width, so a row ends at the grid's edge on any window size.
            var width = Math.Max(1, Math.Min(inner, (inner - ((columns - 1) * gap)) / columns));
            var origin = AutoScrollPosition;
            var y = 0;
            for (var start = 0; start < cards.Count; start += columns)
            {
                var row = cards.Skip(start).Take(columns).ToList();
                var height = row.Max(card => card is IGridCard measured ? measured.MeasureHeight(width) : card.Height);
                for (var i = 0; i < row.Count; i++)
                {
                    var bounds = new Rectangle(origin.X + (i * (width + gap)), origin.Y + y, width, height);
                    if (row[i].Bounds != bounds)
                    {
                        row[i].Bounds = bounds;
                    }
                }

                y += height + gap;
            }

            var minimum = cards.Count == 0 ? Size.Empty : new Size(0, y - gap);
            if (AutoScrollMinSize != minimum)
            {
                AutoScrollMinSize = minimum;
            }
        }
        finally
        {
            _arranging = false;
        }
    }
}

/// <summary>A picture box whose height follows its width in the 2:3 shape of a film poster.</summary>
internal sealed class PosterBox : PictureBox
{
    public PosterBox()
    {
        SizeMode = PictureBoxSizeMode.Zoom;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        BackColor = Theme.Current.SurfaceAlt;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 1 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        return new Size(width, width * 3 / 2);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var height = Width * 3 / 2;
        if (Height != height)
        {
            Height = height;
        }
    }
}

/// <summary>Formats the parts of a film the cards and lists show.</summary>
internal static class FilmText
{
    /// <summary>"2019 · Gerilim".</summary>
    public static string YearAndGenre(Film film)
    {
        ArgumentNullException.ThrowIfNull(film);
        return film.Year.ToString(CultureInfo.InvariantCulture) + " · " + film.Genre;
    }

    /// <summary>"★ 4,3 (12)", or that nobody rated the film yet.</summary>
    public static string Rating(decimal? average, int count) =>
        count == 0 ? "Henüz puan yok" : "★ " + Business.Reviews.RatingMath.Format(average) + " (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>"45,00 TL / gün".</summary>
    public static string DailyPrice(decimal price) => DisplayFormat.Money(price) + " / gün";
}

/// <summary>
/// A film of the catalog as a poster card: the poster (2:3) with a pill when the shelf is empty or holds its
/// last copy, the title over at most two lines, the year and genre, the director, the rating and the daily
/// price, then "Kirala" and a small button that puts the film on the watchlist. The card lays itself out from
/// its fonts, so larger text makes it taller, never cut off.
/// </summary>
internal sealed class PosterCard : CardPanel, IGridCard
{
    private readonly string _fullTitle;
    private readonly Label _title;
    private readonly Label _meta;
    private readonly Label _director;
    private readonly Label _rating;
    private readonly Label _price;
    private readonly string? _pill;
    private readonly RowTone _pillTone;

    public PosterCard(CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var p = Theme.Current;
        var film = item.Film;
        Item = item;
        Name = "filmCard" + film.Id.ToString(CultureInfo.InvariantCulture);
        AccessibleName = film.Title;
        AccessibleRole = AccessibleRole.Grouping;
        Margin = Padding.Empty;

        Poster = new PictureBox
        {
            Name = "picAfis",
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = p.SurfaceAlt,
            TabStop = false,
            AccessibleName = film.Title + " afişi",
            AccessibleRole = AccessibleRole.Graphic,
        };
        (_pill, _pillTone) = film.AvailableCopies switch
        {
            <= 0 => ("Stokta yok", RowTone.Warning),
            1 => ("Son kopya", RowTone.Warm),
            _ => ((string?)null, RowTone.Neutral),
        };
        Poster.Paint += (_, e) => PaintPill(e.Graphics);

        _fullTitle = film.Title;
        _title = Line("lblFilmAdi", film.Title, Theme.Fonts.Subtitle, p.Text);
        _title.AutoEllipsis = false;
        _meta = Line("lblBilgi", FilmText.YearAndGenre(film), Theme.Fonts.Caption, p.TextSecondary);
        _director = Line("lblYonetmen", "Yönetmen: " + film.Director, Theme.Fonts.Caption, p.TextMuted);
        _rating = Line("lblPuan", FilmText.Rating(item.AverageRating, item.RatingCount), Theme.Fonts.Caption, item.RatingCount > 0 ? p.WarmText : p.TextMuted);
        _price = Line("lblUcret", FilmText.DailyPrice(film.DailyPrice), Theme.Fonts.BodyStrong, p.Text);
        _price.TextAlign = ContentAlignment.MiddleRight;

        Rent = new ThemedButton(ButtonKind.Primary)
        {
            Name = "btnKirala",
            Text = item.IsAvailable ? "Kirala" : "Stokta yok",
            Enabled = item.IsAvailable,
            AutoSize = false,
            AccessibleDescription = film.Title,
        };
        AddToList = new ThemedButton(ButtonKind.Secondary)
        {
            Name = "btnListeyeEkle",
            Text = "Listeye ekle",
            Glyph = Glyphs.Watchlist,
            IconOnly = true,
            AutoSize = false,
            AccessibleDescription = film.Title,
        };

        Controls.AddRange([Poster, _title, _meta, _director, _rating, _price, Rent, AddToList]);
    }

    public CatalogItem Item { get; }

    public PictureBox Poster { get; }

    public ThemedButton Rent { get; }

    public ThemedButton AddToList { get; }

    public int MeasureHeight(int width) => Plan(width, 0).Height;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        // The card panel lays out once while its constructor runs, before the parts exist.
        if (AddToList is null)
        {
            return;
        }

        var plan = Plan(Width, Height);
        _title.Text = plan.Title;
        foreach (var (control, bounds) in plan.Bounds)
        {
            if (control.Bounds != bounds)
            {
                control.Bounds = bounds;
            }
        }
    }

    /// <summary>
    /// Where every part of the card goes at <paramref name="width"/>, and the height that needs. The rating, the
    /// price and the buttons sit at the bottom of a card that is taller than it needs (a row of cards shares the
    /// tallest card's height), so the buttons of a row line up.
    /// </summary>
    private (int Height, string Title, List<(Control Control, Rectangle Bounds)> Bounds) Plan(int width, int height)
    {
        var pad = Theme.Px(this, Spacing.M);
        var inner = Math.Max(1, width - (2 * pad));
        var bounds = new List<(Control, Rectangle)>();
        var y = pad;

        var posterHeight = inner * 3 / 2;
        bounds.Add((Poster, new Rectangle(pad, y, inner, posterHeight)));
        y += posterHeight + Theme.Px(this, Spacing.S + 2);

        // The title takes at most two lines; a longer one ends in an ellipsis (the whole title is the card's name).
        var title = FitLines(_fullTitle, _title.Font, inner, 2);
        var titleHeight = Math.Max(_title.Font.Height, TextRenderer.MeasureText(title, _title.Font, new Size(inner, int.MaxValue), WrapFlags).Height);
        bounds.Add((_title, new Rectangle(pad, y, inner, titleHeight)));
        y += titleHeight + Theme.Px(this, 2);

        foreach (var line in new[] { _meta, _director })
        {
            bounds.Add((line, new Rectangle(pad, y, inner, line.Font.Height)));
            y += line.Font.Height;
        }

        // The bottom block: rating and price, then the buttons.
        var rowHeight = Math.Max(_rating.Font.Height, _price.Font.Height);
        var buttonHeight = Math.Max(Theme.Px(this, Sizes.ControlHeight), Rent.Font.Height + Theme.Px(this, Spacing.S + 2));
        var gap = Theme.Px(this, Spacing.S);
        var iconOnly = AddToList.ShowsIconOnly && inner - buttonHeight - gap >= Theme.Px(this, Sizes.ButtonMinWidth);
        var buttonsHeight = iconOnly ? buttonHeight : (2 * buttonHeight) + gap;
        var block = rowHeight + Theme.Px(this, Spacing.M) + buttonsHeight;
        var natural = y + Theme.Px(this, Spacing.S) + block + pad;
        y = Math.Max(y + Theme.Px(this, Spacing.S), height - pad - block);

        // The price keeps its whole text; the rating gets what is left.
        var priceWidth = Math.Min(inner, _price.GetPreferredSize(Size.Empty).Width);
        var ratingWidth = Math.Max(0, inner - priceWidth - gap);
        bounds.Add((_rating, new Rectangle(pad, y, ratingWidth, rowHeight)));
        bounds.Add((_price, new Rectangle(pad + inner - priceWidth, y, priceWidth, rowHeight)));
        y += rowHeight + Theme.Px(this, Spacing.M);

        // "Kirala" fills the row beside the square watchlist button; where that button shows its text (high
        // contrast) or the row is too narrow, each button takes a row of its own.
        if (iconOnly)
        {
            bounds.Add((Rent, new Rectangle(pad, y, inner - buttonHeight - gap, buttonHeight)));
            bounds.Add((AddToList, new Rectangle(pad + inner - buttonHeight, y, buttonHeight, buttonHeight)));
        }
        else
        {
            bounds.Add((Rent, new Rectangle(pad, y, inner, buttonHeight)));
            bounds.Add((AddToList, new Rectangle(pad, y + buttonHeight + gap, inner, buttonHeight)));
        }

        return (natural, title, bounds);
    }

    private const TextFormatFlags WrapFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    /// <summary><paramref name="text"/> as it fits <paramref name="lines"/> lines of <paramref name="width"/>, shortened with an ellipsis.</summary>
    internal static string FitLines(string text, Font font, int width, int lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        var limit = lines * font.Height;
        bool Fits(string candidate) => TextRenderer.MeasureText(candidate, font, new Size(Math.Max(1, width), int.MaxValue), WrapFlags).Height <= limit;
        if (Fits(text))
        {
            return text;
        }

        var words = text.Split(' ');
        for (var count = words.Length - 1; count > 0; count--)
        {
            var candidate = string.Join(' ', words.Take(count)).TrimEnd(',', ':', ';', '-') + "…";
            if (Fits(candidate))
            {
                return candidate;
            }
        }

        // A single word longer than the lines: cut it character by character.
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length] + "…";
            if (Fits(candidate))
            {
                return candidate;
            }
        }

        return "…";
    }
    private static Label Line(string name, string text, Font font, Color color) => new()
    {
        Name = name,
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        Font = font,
        ForeColor = color,
        Margin = Padding.Empty,
    };

    /// <summary>The stock pill at the poster's top left corner; under high contrast an outline in the text color.</summary>
    private void PaintPill(Graphics graphics)
    {
        if (_pill is null)
        {
            return;
        }

        var p = Theme.Current;
        var (fill, border, text) = ThemedListBox.PillColors(p, selected: false, _pillTone);
        var font = Theme.Fonts.Caption;
        var size = TextRenderer.MeasureText(graphics, _pill, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var inset = Theme.Px(this, Spacing.S);
        var pill = new Rectangle(inset, inset, size.Width + (2 * Theme.Px(this, Spacing.S)), font.Height + Theme.Px(this, 6));
        Painting.FillRounded(graphics, pill, pill.Height / 2F, fill, border, Math.Max(1F, Theme.PxF(this, 1)));
        TextRenderer.DrawText(graphics, _pill, font, pill, text, fill, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// An active rental as a card: a small poster, the film, the rental and due days and the price, and the
/// button that returns it. Used by the customer's profile.
/// </summary>
internal sealed class RentalCard : CardPanel, IGridCard
{
    private readonly Label _title;
    private readonly Label _dates;
    private readonly Label _price;
    private readonly Label? _tenant;

    /// <param name="showTenant">Whether the card names the account that rented the film (the admin's list).</param>
    public RentalCard(RentalDetails rental, string actionText, bool showTenant = false)
    {
        ArgumentNullException.ThrowIfNull(rental);

        var p = Theme.Current;
        Rental = rental;
        Name = "rentalCard" + rental.Id.ToString(CultureInfo.InvariantCulture);
        AccessibleName = rental.FilmTitle;
        AccessibleRole = AccessibleRole.Grouping;
        Margin = Padding.Empty;
        Poster = new PictureBox
        {
            Name = "picAfis",
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = p.SurfaceAlt,
            TabStop = false,
            AccessibleName = rental.FilmTitle + " afişi",
            AccessibleRole = AccessibleRole.Graphic,
        };
        _title = Line("lblFilmAdi", rental.FilmTitle, Theme.Fonts.Subtitle, p.Text);
        _dates = Line("lblTarihler", "Alış " + DisplayFormat.Date(rental.RentedOn) + " · İade " + DisplayFormat.Date(rental.DueOn), Theme.Fonts.Caption, p.TextSecondary);
        _price = Line("lblUcret", rental.Genre + " · " + DisplayFormat.Money(rental.Price), Theme.Fonts.Caption, p.TextMuted);
        Action = new ThemedButton(ButtonKind.Secondary) { Name = "btnIade", Text = actionText, Glyph = Glyphs.Return, AutoSize = false, AccessibleDescription = rental.FilmTitle };
        Controls.AddRange([Poster, _title, _dates, _price, Action]);
        if (showTenant)
        {
            _tenant = Line("lblKiraci", "Kiracı: " + rental.UserName + " (No " + rental.UserId.ToString(CultureInfo.InvariantCulture) + ")", Theme.Fonts.Caption, p.TextSecondary);
            Controls.Add(_tenant);
        }
    }

    public RentalDetails Rental { get; }

    public PictureBox Poster { get; }

    public ThemedButton Action { get; }

    public int MeasureHeight(int width) => Plan(width).Height;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        // The card panel lays out once while its constructor runs, before the parts exist.
        if (Action is null)
        {
            return;
        }

        foreach (var (control, bounds) in Plan(Width).Bounds)
        {
            if (control.Bounds != bounds)
            {
                control.Bounds = bounds;
            }
        }
    }

    private (int Height, List<(Control Control, Rectangle Bounds)> Bounds) Plan(int width)
    {
        var pad = Theme.Px(this, Spacing.M);
        var thumbWidth = Theme.Px(this, Sizes.PosterThumbWidth);
        var thumb = new Rectangle(pad, pad, thumbWidth, thumbWidth * 3 / 2);
        var left = thumb.Right + Theme.Px(this, Spacing.M);
        var inner = Math.Max(1, width - left - pad);
        var bounds = new List<(Control, Rectangle)> { (Poster, thumb) };

        var y = pad;
        bounds.Add((_title, new Rectangle(left, y, inner, _title.Font.Height)));
        y += _title.Font.Height + Theme.Px(this, Spacing.XS);
        bounds.Add((_dates, new Rectangle(left, y, inner, _dates.Font.Height)));
        y += _dates.Font.Height;
        bounds.Add((_price, new Rectangle(left, y, inner, _price.Font.Height)));
        y += _price.Font.Height;
        if (_tenant is not null)
        {
            bounds.Add((_tenant, new Rectangle(left, y, inner, _tenant.Font.Height)));
            y += _tenant.Font.Height;
        }

        y += Theme.Px(this, Spacing.M);

        var preferred = Action.GetPreferredSize(Size.Empty);
        var buttonHeight = Math.Max(Theme.Px(this, Sizes.ControlHeight), preferred.Height);
        bounds.Add((Action, new Rectangle(left, y, Math.Min(inner, Math.Max(preferred.Width, Theme.Px(this, Sizes.ButtonMinWidth))), buttonHeight)));
        y += buttonHeight;

        return (Math.Max(thumb.Bottom, y) + pad, bounds);
    }

    private static Label Line(string name, string text, Font font, Color color) => new()
    {
        Name = name,
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        Font = font,
        ForeColor = color,
        Margin = Padding.Empty,
    };
}
