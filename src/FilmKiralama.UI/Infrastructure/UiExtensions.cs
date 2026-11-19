using FilmKiralama.Business.Validation;

namespace FilmKiralama.UI.Infrastructure;

internal static class UiExtensions
{
    /// <summary>
    /// Runs an asynchronous UI action with a wait cursor. Business rule violations are shown as
    /// warnings; anything else is reported through <see cref="ErrorReporter"/>.
    /// </summary>
    public static async Task RunAsync(this Form form, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(action);

        form.UseWaitCursor = true;
        try
        {
            await action();
        }
#pragma warning disable CA1031 // UI boundary: every failure is reported to the user.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorReporter.Show(ex, form);
        }
        finally
        {
            form.UseWaitCursor = false;
        }
    }

    /// <summary>
    /// Like <see cref="RunAsync"/>, but through <paramref name="operation"/>: while a run is pending a new
    /// call is ignored and <paramref name="controls"/> stay disabled, so a write cannot be started twice.
    /// A run that starts clears the previous inline message of the screen.
    /// </summary>
    public static Task<bool> RunExclusiveAsync(
        this Form form,
        ExclusiveOperation operation,
        Func<Task> action,
        params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        return operation.RunAsync(
            () =>
            {
                (form as IStatusSurface)?.ClearStatus();
                return form.RunAsync(action);
            },
            controls);
    }

    /// <summary>Lets only digits (and editing keys) be typed.</summary>
    public static void AcceptDigitsOnly(this TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>Lets only digits and one decimal comma (and editing keys) be typed, for prices.</summary>
    public static void AcceptMoneyOnly(this TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.KeyPress += (_, e) =>
        {
            var comma = e.KeyChar == ',' && !textBox.Text.Contains(',', StringComparison.Ordinal);
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar) && !comma)
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Formats a phone field as the original forms did ("555 123 4567", at most ten digits) while it is
    /// typed. The services accept the number with or without the spaces.
    /// </summary>
    public static void FormatAsPhone(this TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.AcceptDigitsOnly();
        var formatting = false;
        textBox.TextChanged += (_, _) =>
        {
            if (formatting)
            {
                return;
            }

            formatting = true;
            try
            {
                textBox.Text = FormatPhone(textBox.Text);
                textBox.SelectionStart = textBox.Text.Length;
            }
            finally
            {
                formatting = false;
            }
        };
    }

    /// <summary>The digits of <paramref name="value"/> (at most ten) grouped as 3, 3 and 4.</summary>
    public static string FormatPhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).Take(InputRules.PhoneDigits).ToArray());
        return digits.Length switch
        {
            <= 3 => digits,
            <= 6 => digits[..3] + " " + digits[3..],
            _ => digits[..3] + " " + digits[3..6] + " " + digits[6..],
        };
    }

    /// <summary>
    /// Shows a film's poster: an http or https address on a public host is downloaded in the background
    /// by <see cref="PosterLoader"/>, anything else (or a refused or failed download) shows the
    /// application's own placeholder. Local paths, network shares and local network addresses are never
    /// opened, as the address comes from the database.
    /// </summary>
    public static void ShowPoster(this PictureBox pictureBox, string? imageUrl) =>
        PosterLoader.Default.Show(pictureBox, imageUrl);

    /// <summary>
    /// Read-only list settings of the original grids. Under high contrast the system colors stay, so a
    /// selected row never gets a fixed light text color on the highlight.
    /// </summary>
    public static void UseReadOnlyList(this DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        if (SystemInformation.HighContrast)
        {
            grid.DefaultCellStyle.ForeColor = SystemColors.WindowText;
            grid.DefaultCellStyle.BackColor = SystemColors.Window;
        }
        else
        {
            grid.DefaultCellStyle.ForeColor = Color.Black;
        }

        grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
    }

    /// <summary>Replaces the items of a list without selecting one, keeping the update to a single repaint.</summary>
    public static void ReplaceItems<T>(this ListBox listBox, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(items);

        listBox.BeginUpdate();
        try
        {
            listBox.Items.Clear();
            foreach (var item in items)
            {
                listBox.Items.Add(item!);
            }

            listBox.SelectedIndex = -1;
        }
        finally
        {
            listBox.EndUpdate();
        }
    }

    /// <summary>Replaces the items of a combo box, keeping the update to a single repaint.</summary>
    public static void ReplaceItems<T>(this ComboBox comboBox, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(items);

        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            foreach (var item in items)
            {
                comboBox.Items.Add(item!);
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }
}
