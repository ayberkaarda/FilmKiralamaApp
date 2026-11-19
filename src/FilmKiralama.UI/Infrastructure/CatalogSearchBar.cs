using System.Globalization;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// The search fields of the film lists: title, genre, year range, minimum rating and "available only".
/// Filling the choices is not a user choice: no change is reported while they are replaced, and the
/// previous choices are kept when they still exist. Typing in the title is reported once the typing
/// pauses for <see cref="TitleDelay"/> (or at Enter), not on every key, so the database is not asked
/// once per character; any other change is reported at once and includes the title typed so far.
/// </summary>
internal sealed class CatalogSearchBar : Panel
{
    /// <summary>Pause in typing after which the title is searched.</summary>
    public static readonly TimeSpan TitleDelay = TimeSpan.FromMilliseconds(300);

    private readonly System.Windows.Forms.Timer _titleTimer = new() { Interval = (int)TitleDelay.TotalMilliseconds };
    private readonly TextBox _title;
    private readonly ComboBox _genre;
    private readonly ComboBox _yearFrom;
    private readonly ComboBox _yearTo;
    private readonly ComboBox _rating;
    private readonly CheckBox _availableOnly;
    private bool _filling;

    /// <summary>The fields in one row at <paramref name="location"/>, captions above them.</summary>
    public CatalogSearchBar(Point location, Color captionColor, bool availableOnly)
    {
        Name = "pnlArama";
        Location = location;
        Size = new Size(840, 50);
        BackColor = Color.Transparent;
        var origin = Point.Empty;

        Label Caption(string text, int x) => new()
        {
            Text = text,
            Location = new Point(origin.X + x, origin.Y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = captionColor,
            BackColor = Color.Transparent,
        };

        var top = origin.Y + 20;
        _title = new TextBox { Name = "txtAra", Location = new Point(origin.X, top), Width = 200, MaxLength = InputRules.TitleMaxLength };
        _genre = Box("turCombo", origin.X + 215, top, 140);
        _yearFrom = Box("yilBaslangicCombo", origin.X + 370, top, 90);
        _yearTo = Box("yilBitisCombo", origin.X + 470, top, 90);
        _rating = Box("puanCombo", origin.X + 575, top, 110);
        _availableOnly = new CheckBox
        {
            Name = "chkMusait",
            Text = "Yalnızca müsait",
            Location = new Point(origin.X + 700, top + 2),
            AutoSize = true,
            Checked = availableOnly,
            ForeColor = captionColor,
            BackColor = Color.Transparent,
        };

        foreach (var box in new[] { _genre, _yearFrom, _yearTo })
        {
            box.Items.Add(FilterChoice.Any);
            box.SelectedIndex = 0;
        }

        _rating.Items.Add(FilterChoice.Any);
        for (var stars = 5; stars >= 1; stars--)
        {
            var value = stars.ToString(CultureInfo.InvariantCulture);
            _rating.Items.Add(new FilterChoice(stars == 5 ? "5 ★" : value + "+ ★", value));
        }

        _rating.SelectedIndex = 0;

        _title.TextChanged += (_, _) => RestartTitleTimer();
        _title.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                CommitTitle();
            }
        };
        _titleTimer.Tick += (_, _) => CommitTitle();
        foreach (var box in new[] { _genre, _yearFrom, _yearTo, _rating })
        {
            box.SelectedIndexChanged += (_, _) => Raise();
        }

        _availableOnly.CheckedChanged += (_, _) => Raise();
        Controls.AddRange(
        [
            Caption("Film adı", 0), _title,
            Caption("Tür", 215), _genre,
            Caption("Yıldan", 370), _yearFrom,
            Caption("Yıla", 470), _yearTo,
            Caption("En düşük puan", 575), _rating,
            _availableOnly,
        ]);
    }

    /// <summary>Raised when the user changes a field; for the title, once the typing pauses.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether a typed title waits for the pause before it is searched.</summary>
    public bool TitlePending => _titleTimer.Enabled;

    /// <summary>Searches a typed title now instead of after the pause; nothing happens when none waits.</summary>
    public void CommitTitle()
    {
        if (_titleTimer.Enabled)
        {
            Raise();
        }
    }

    /// <summary>The search of the fields, on the first page.</summary>
    public CatalogQuery Current => new(
        string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text.Trim(),
        Selected(_genre),
        Year(_yearFrom),
        Year(_yearTo),
        Selected(_rating) is { } rating ? int.Parse(rating, CultureInfo.InvariantCulture) : null,
        _availableOnly.Checked);

    /// <summary>Replaces the genre and year choices, keeping what was chosen when it still exists.</summary>
    public void Fill(FilmFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filling = true;
        try
        {
            Fill(_genre, options.Genres);
            var years = options.Years.OrderBy(y => y).Select(y => y.ToString(CultureInfo.InvariantCulture)).ToList();
            Fill(_yearFrom, years);
            Fill(_yearTo, years);
        }
        finally
        {
            _filling = false;
        }
    }

    private static ComboBox Box(string name, int x, int y, int width) =>
        new() { Name = name, Location = new Point(x, y), Width = width, DropDownStyle = ComboBoxStyle.DropDownList };

    private static void Fill(ComboBox box, IEnumerable<string> values)
    {
        var previous = Selected(box);
        box.ReplaceItems(new[] { FilterChoice.Any }.Concat(values.Select(v => new FilterChoice(v, v))));
        var index = 0;
        for (var i = 1; i < box.Items.Count; i++)
        {
            if (box.Items[i] is FilterChoice choice && string.Equals(choice.Value, previous, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        box.SelectedIndex = index;
    }

    private static string? Selected(ComboBox box) => (box.SelectedItem as FilterChoice)?.Value;

    private static int? Year(ComboBox box) => Selected(box) is { } year ? int.Parse(year, CultureInfo.InvariantCulture) : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RestartTitleTimer()
    {
        _titleTimer.Stop();
        _titleTimer.Start();
    }

    /// <summary>Reports a change, with the title as typed so far; a waiting title is reported with it.</summary>
    private void Raise()
    {
        if (!_filling)
        {
            _titleTimer.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
