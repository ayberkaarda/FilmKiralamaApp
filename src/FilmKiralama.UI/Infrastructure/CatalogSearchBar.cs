using System.Globalization;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Theming;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// The search fields of the film lists in one row: title, genre, year range, minimum rating and "available
/// only". Filling the choices is not a user choice: no change is reported while they are replaced, and the
/// previous choices are kept when they still exist. Typing in the title is reported once the typing pauses
/// for <see cref="TitleDelay"/> (or at Enter), not on every key, so the database is not asked once per
/// character; any other change is reported at once and includes the title typed so far.
/// </summary>
internal sealed class CatalogSearchBar : TableLayoutPanel
{
    /// <summary>Pause in typing after which the title is searched.</summary>
    public static readonly TimeSpan TitleDelay = TimeSpan.FromMilliseconds(300);

    private readonly System.Windows.Forms.Timer _titleTimer = new() { Interval = (int)TitleDelay.TotalMilliseconds };
    private readonly TextBox _title = new() { Name = "txtAra", MaxLength = InputRules.TitleMaxLength };
    private readonly ThemedComboBox _genre = FormLayout.Picker("turCombo", 150);
    private readonly ThemedComboBox _yearFrom = FormLayout.Picker("yilBaslangicCombo", 96);
    private readonly ThemedComboBox _yearTo = FormLayout.Picker("yilBitisCombo", 96);
    private readonly ThemedComboBox _rating = FormLayout.Picker("puanCombo", 120);
    private readonly ThemedCheckBox _availableOnly = new() { Name = "chkMusait", Text = "Yalnızca &müsait" };
    private bool _filling;

    public CatalogSearchBar(bool availableOnly)
    {
        Name = "pnlArama";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
        ColumnCount = 6;
        RowCount = 1;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _availableOnly.Checked = availableOnly;

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

        (Control Field, float Percent)[] columns =
        [
            (FormLayout.Field("&Film adı", FormLayout.Input(_title)), 34),
            (FormLayout.Field("&Tür", _genre), 20),
            (FormLayout.Field("Yıl&dan", _yearFrom), 12),
            (FormLayout.Field("Yıl&a", _yearTo), 12),
            (FormLayout.Field("En düşük &puan", _rating), 16),
            (FormLayout.FieldAction(_availableOnly), 0),
        ];
        for (var i = 0; i < columns.Length; i++)
        {
            var (field, percent) = columns[i];
            if (i == columns.Length - 1)
            {
                field.Margin = new Padding(field.Margin.Left, field.Margin.Top, 0, field.Margin.Bottom);
            }

            ColumnStyles.Add(percent > 0 ? new ColumnStyle(SizeType.Percent, percent) : new ColumnStyle(SizeType.AutoSize));
            Controls.Add(field, i, 0);
        }

        // The check box lines up with the inputs' center line.
        _availableOnly.Margin = new Padding(0, Spacing.XS + ((Sizes.ControlHeight - 22) / 2), 0, 0);

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
    }

    /// <summary>Raised when the user changes a field; for the title, once the typing pauses.</summary>
    public event EventHandler? Changed;

    /// <summary>The title box, the first stop of the keyboard.</summary>
    public TextBox TitleBox => _title;

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
