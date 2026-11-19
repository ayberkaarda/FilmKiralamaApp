using FilmKiralama.Business.Reports;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>The report screen, the CSV file it saves and the recommendations; no dialog or window opens.</summary>
public sealed class ReportUiTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "filmkiralama-report-test-" + Guid.NewGuid().ToString("N"));

    public ReportUiTests() => Directory.CreateDirectory(_directory);

    private static FakeTimeProvider Clock => new(new DateTimeOffset(2026, 3, 31, 10, 0, 0, TimeSpan.Zero));

    /// <summary>The CSV holds the report that is shown, for the period it was shown with, even after the fields changed.</summary>
    [Fact]
    public void Csv_UsesThePeriodOfTheShownReport() => Run(() =>
    {
        var reports = new ControlledReportService();
        var path = Path.Combine(_directory, "rapor.csv");
        using var form = new RaporlarForm(reports, Sessions.Admin, Clock) { Prompts = new RecordingPrompts(), SavePrompt = new FixedSavePrompt(path) };
        var load = form.LoadAsync();
        reports.MostRentedRequests[0].Answer.SetResult([new RentalCountRow(1, "Sisli Liman", "Gerilim", 4)]);
        Assert.True(load.IsCompletedSuccessfully);
        var shown = reports.MostRentedRequests[0].Range;
        Assert.Equal(new ReportRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)), shown);

        Find<DateTimePicker>(form, "dtpBaslangic").Value = new DateTime(2025, 1, 1);
        Assert.True(form.ExportCsvAsync().Result);

        Assert.Equal([(ReportKind.MostRented, shown)], reports.Exports);
        Assert.Equal(1, reports.RecordedExports);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)';', (byte)'b' }, File.ReadAllBytes(path));
        Assert.Equal(["rapor.csv"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    });

    [Fact]
    public void Csv_SecondSaveWhilePending_IsIgnored() => Run(() =>
    {
        var reports = new ControlledReportService { HoldExports = true };
        using var form = new RaporlarForm(reports, Sessions.Admin, Clock) { Prompts = new RecordingPrompts(), SavePrompt = new FixedSavePrompt(Path.Combine(_directory, "a.csv")) };
        _ = form.LoadAsync();
        reports.MostRentedRequests[0].Answer.SetResult([]);

        var first = form.ExportCsvAsync();
        var second = form.ExportCsvAsync();

        Assert.False(second.Result);
        Assert.Single(reports.Exports);
        Assert.False(Find<Button>(form, "btnCsv").Enabled);
        Assert.False(Find<Button>(form, "btnGoster").Enabled);
        reports.CompleteExport();
        Assert.True(SpinUntil(() => first.IsCompleted));
        Assert.True(first.Result);
    });

    [Fact]
    public void Csv_CancelledDialog_WritesNothing() => Run(() =>
    {
        var reports = new ControlledReportService();
        var prompts = new RecordingPrompts();
        using var form = new RaporlarForm(reports, Sessions.Admin, Clock) { Prompts = prompts, SavePrompt = new FixedSavePrompt(null) };
        Assert.False(Find<Button>(form, "btnCsv").Enabled);
        _ = form.LoadAsync();
        reports.MostRentedRequests[0].Answer.SetResult([]);

        Assert.True(form.ExportCsvAsync().Result);

        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Empty(prompts.Messages);
        Assert.Equal(0, reports.RecordedExports);
    });

    /// <summary>Two loads answered out of order show the later one; the overdue report has no period.</summary>
    [Fact]
    public void Reports_LatestLoadWinsAndOverdueHasNoPeriod() => Run(() =>
    {
        var reports = new ControlledReportService();
        using var form = new RaporlarForm(reports, Sessions.Admin, Clock) { Prompts = new RecordingPrompts(), SavePrompt = new FixedSavePrompt(null) };
        var grid = Find<DataGridView>(form, "dgvRapor");
        _ = grid.Handle;

        var first = form.LoadAsync();
        var second = form.LoadAsync();
        reports.MostRentedRequests[1].Answer.SetResult([new RentalCountRow(2, "Son Vapur", "Dram", 1)]);
        reports.MostRentedRequests[0].Answer.SetResult([new RentalCountRow(1, "Sisli Liman", "Gerilim", 4), new RentalCountRow(2, "Son Vapur", "Dram", 1)]);

        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully);
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Empty(grid.SelectedRows);
        Assert.Equal("1 film, toplam 1 kiralama", Find<Label>(form, "lblToplam").Text);

        Find<ComboBox>(form, "cmbRapor").SelectedIndex = 1;
        Assert.False(Find<DateTimePicker>(form, "dtpBaslangic").Enabled);
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Assert.Equal("1 geciken kiralama, birikmiş gecikme ücreti 50,00 TL", Find<Label>(form, "lblToplam").Text);
    });

    /// <summary>A file is written next to the target and moved over it: the old content stays until the new one is complete.</summary>
    [Fact]
    public async Task ExportFileWriter_ReplacesTheFileAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(_directory, "rapor.csv");
        await File.WriteAllTextAsync(path, "eski", TestContext.Current.CancellationToken);

        await ExportFileWriter.WriteAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
        await Assert.ThrowsAnyAsync<IOException>(() => ExportFileWriter.WriteAsync(Path.Combine(_directory, "yok", "rapor.csv"), [1], TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Recommendations_AreBoundWithoutASelection() => Run(() =>
    {
        var recommendations = new ControlledRecommendationService();
        using var form = new OnerilerForm(recommendations, new ControlledWatchlistService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dgvOneriler");
        _ = grid.Handle;

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Empty(grid.SelectedRows);
        Assert.False(Find<Button>(form, "btnKirala").Enabled);
        Assert.False(Find<Button>(form, "btnListeyeEkle").Enabled);

        recommendations.Films.Clear();
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Assert.StartsWith("Henüz öneri yok", Find<Label>(form, "lblOzet").Text, StringComparison.Ordinal);
    });

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>The file write continues on the thread pool; waits a little for it without a message loop.</summary>
    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10));
}
