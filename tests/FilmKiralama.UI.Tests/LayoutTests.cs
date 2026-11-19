using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using FilmKiralama.UI.Theming;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>
/// Every screen keeps each control inside its parent, without overlaps, on a small laptop, a common laptop
/// and a large screen at four text sizes; long titles and user names never hide a state. Nothing is shown.
/// </summary>
public sealed class LayoutTests
{
    public static TheoryData<string> ScreenNames => [.. TestForms.Names];

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public void Screen_FitsEveryScreenAtEveryTextSize(string name) => Run(() =>
        LayoutAssert.FitsInsideOnEveryScreen(() => TestForms.Create(name)));

    /// <summary>The pill keeps its whole width at the right of a narrow line; the name gets what is left, never the pill's room.</summary>
    [Theory]
    [InlineData(400, 90)]
    [InlineData(120, 90)]
    [InlineData(60, 90)]
    public void StatusPill_NeverGivesWayToTheName(int lineWidth, int pillWidth)
    {
        var line = new Rectangle(10, 5, lineWidth, 20);

        var (primary, status) = ThemedListBox.SplitLine(line, pillWidth, 8);

        Assert.Equal(line.Right, status.Right);
        Assert.Equal(Math.Min(pillWidth, lineWidth), status.Width);
        Assert.False(primary.IntersectsWith(status));
        Assert.True(primary.Width >= 0);
        Assert.Equal(line, ThemedListBox.SplitLine(line, 0, 8).Primary);
    }

    /// <summary>
    /// The films of a row share the row's height and fill the grid's width; a narrower grid has fewer columns,
    /// never a card cut off at the right edge.
    /// </summary>
    [Theory]
    [InlineData(1100)]
    [InlineData(700)]
    [InlineData(150)]
    public void CardGrid_WrapsCardsToItsWidth(int width) => Run(() =>
    {
        using var form = TestForms.Catalog();
        LayoutAssert.CreateHandles(form);
        var grid = TestForms.Get<CardGrid>(form, "pnlFilmler");
        grid.Dock = DockStyle.None;
        grid.Width = width;
        grid.PerformLayout();

        var cards = grid.Controls.Cast<Control>().ToList();
        Assert.Equal(3, cards.Count);
        Assert.All(cards, card => Assert.True(card.Right <= grid.ClientSize.Width, $"{card.Name} ends at {card.Right} in a grid {grid.ClientSize.Width} wide."));
        foreach (var row in cards.GroupBy(card => card.Top))
        {
            Assert.Single(row.Select(card => card.Height).Distinct());
        }

        // The buttons of a row line up, whatever the length of the titles above them.
        var rents = cards.Select(card => TestForms.Get<Button>(card, "btnKirala")).GroupBy(button => button.Parent!.Top);
        Assert.All(rents, row => Assert.Single(row.Select(button => button.Top).Distinct()));
    });

    /// <summary>A message window never opens larger than the working area of its screen; its content scrolls instead.</summary>
    [Fact]
    public void MessageDialog_StaysInsideASmallWorkingArea() => Run(() =>
    {
        var previous = AppDialog.WorkingAreaOf;
        var area = new Rectangle(0, 0, 400, 240);
        AppDialog.WorkingAreaOf = _ => area;
        try
        {
            var message = string.Join(" ", Enumerable.Repeat("Bu uzun bir açıklama satırıdır.", 60));
            using var dialog = new MessageDialog(message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            typeof(Form).GetMethod("OnLoad", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [EventArgs.Empty]);

            Assert.True(dialog.Width <= area.Width && dialog.Height <= area.Height, $"The dialog opens at {dialog.Size}.");
            Assert.True(dialog.AutoScroll);
        }
        finally
        {
            AppDialog.WorkingAreaOf = previous;
        }
    });
}
