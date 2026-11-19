using System.ComponentModel;

namespace FilmKiralama.UI.Theming;

/// <summary>
/// A read-only table with the theme's style: a quiet header, horizontal row dividers, whole-row selection
/// and a message in place of the rows while the table loads or when it is empty. Binding rows never
/// selects one: the screen decides what is selected. Under high contrast the cells use the system colors,
/// and a selected row is HighlightText on Highlight, never a palette color.
/// </summary>
internal sealed class ThemedGrid : DataGridView, IThemeAware
{
    private bool _loading;
    private string _emptyText = string.Empty;

    public ThemedGrid()
    {
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = false;
        RowHeadersVisible = false;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        StandardTab = true; // Tab leaves the table instead of walking its cells.
        DoubleBuffered = true;
        Margin = Padding.Empty;

        // A table that fills a card keeps room for a header and a few rows; a smaller window scrolls instead.
        MinimumSize = new Size(0, 140);
        ApplyStyle(this);
    }

    /// <summary>Shown in the middle of the table while it has no rows.</summary>
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

    /// <summary>A list request is on its way; an empty table says so instead of "nothing here".</summary>
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

    /// <summary>
    /// How wide the generated columns are relative to each other, by property name (100 is the default share);
    /// applied every time rows are bound.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyDictionary<string, float> ColumnWeights { get; set; } = new Dictionary<string, float>();

    /// <summary>
    /// The color a cell's text takes, for example a state column; <see langword="null"/> keeps the table's color.
    /// Ignored under high contrast, where the system colors stay.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, object?, Color?>? CellTone { get; set; }

    /// <summary>The message the table shows now instead of rows, or an empty text.</summary>
    internal string ShownMessage => Rows.Count > 0 ? string.Empty : _loading ? LoadingText : _emptyText;

    void IThemeAware.ApplyTheme(ThemeRemap remap)
    {
        ApplyStyle(this);
        Invalidate();
    }

    /// <summary>
    /// The table style for the current palette; padding follows the control's DPI. Under high contrast every
    /// color goes back to the system: the selection pair is Highlight and HighlightText.
    /// </summary>
    internal static void ApplyStyle(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var p = Theme.Current;
        grid.Font = Theme.Fonts.Body;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        var cellPadding = new Padding(Theme.Px(grid, Spacing.S), Theme.Px(grid, 5), Theme.Px(grid, Spacing.S), Theme.Px(grid, 5));
        var header = grid.ColumnHeadersDefaultCellStyle;
        var cells = grid.DefaultCellStyle;
        header.Padding = cellPadding;
        header.Font = Theme.Fonts.BodyStrong;
        header.WrapMode = DataGridViewTriState.False;
        cells.Padding = cellPadding;
        cells.WrapMode = DataGridViewTriState.False;

        if (p.IsHighContrast)
        {
            grid.EnableHeadersVisualStyles = true;
            grid.BackgroundColor = SystemColors.Window;
            grid.GridColor = SystemColors.WindowText;
            header.BackColor = SystemColors.Control;
            header.ForeColor = SystemColors.ControlText;
            header.SelectionBackColor = SystemColors.Highlight;
            header.SelectionForeColor = SystemColors.HighlightText;
            cells.BackColor = SystemColors.Window;
            cells.ForeColor = SystemColors.WindowText;
            cells.SelectionBackColor = SystemColors.Highlight;
            cells.SelectionForeColor = SystemColors.HighlightText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.Empty;
            return;
        }

        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = p.Surface;
        grid.GridColor = p.Border;
        header.BackColor = p.SurfaceAlt;
        header.ForeColor = p.TextSecondary;
        header.SelectionBackColor = p.SurfaceAlt;
        header.SelectionForeColor = p.TextSecondary;
        cells.BackColor = p.Surface;
        cells.ForeColor = p.Text;
        cells.SelectionBackColor = p.AccentSubtle;
        cells.SelectionForeColor = p.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.Empty;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // The padding was measured at the DPI known before the window existed.
        ApplyStyle(this);
    }

    protected override void OnDataBindingComplete(DataGridViewBindingCompleteEventArgs e)
    {
        base.OnDataBindingComplete(e);
        foreach (DataGridViewColumn column in Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (ColumnWeights.TryGetValue(column.DataPropertyName, out var weight))
            {
                column.FillWeight = weight;
            }
        }

        Invalidate();
    }

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnCellFormatting(e);
        if (Theme.Current.IsHighContrast || CellTone is null || e.CellStyle is null || e.ColumnIndex < 0 || e.RowIndex < 0)
        {
            return;
        }

        if (CellTone(Columns[e.ColumnIndex].DataPropertyName, e.Value) is { } color)
        {
            // A state stays in its color on a selected row too; the selection there is only a tint.
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
            e.CellStyle.Font = Theme.Fonts.BodyStrong;
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyStyle(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        PaintMessage(e.Graphics);
    }

    /// <summary>
    /// Paints the empty or loading message into <paramref name="graphics"/> while the table has no rows.
    /// DrawToBitmap (used by the offscreen screenshot tool) prints the native table only, so it needs this entry point.
    /// </summary>
    internal void PaintMessage(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var message = ShownMessage;
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var top = ColumnHeadersVisible && ColumnCount > 0 ? ColumnHeadersHeight : 0;
        var pad = Theme.Px(this, Spacing.L);
        var area = new Rectangle(pad, top + pad, Math.Max(0, Width - (2 * pad)), Math.Max(0, Height - top - (2 * pad)));
        TextRenderer.DrawText(
            graphics,
            message,
            Theme.Fonts.Body,
            area,
            Theme.Current.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
