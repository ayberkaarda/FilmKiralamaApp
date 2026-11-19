using System.Text;
using FilmKiralama.Business.Reports;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>Reports, their CSV and the recommendations, with strict mocks.</summary>
public sealed class ReportServiceTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);
    private static readonly ReportRange March = new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

    private readonly Mock<IReportRepository> _reports = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reports_AreForAdminsOnly()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetMostRentedAsync(Sessions.User, March, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetOverdueAsync(Sessions.User, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetLateFeesAsync(Sessions.User, March, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().ExportCsvAsync(Sessions.User, ReportKind.MostRented, March, Ct));

        _reports.VerifyNoOtherCalls();
        Assert.All(_audit.Entries, e => Assert.Equal("access.denied user", e));
    }

    [Fact]
    public async Task Reports_ForARevokedAdmin_AskToSignInAgain()
    {
        _reports.Setup(r => r.GetLateFeesAsync(Sessions.Admin.UserId, March.From, March.To, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());

        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetLateFeesAsync(Sessions.Admin, March, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(ReportPolicy.MaxRangeDays - 1, true)]
    [InlineData(ReportPolicy.MaxRangeDays, false)]
    [InlineData(-1, false)]
    public async Task Range_FromOneDayToTheLimit_IsAccepted(int days, bool accepted)
    {
        var range = new ReportRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1).AddDays(days));
        _reports.Setup(r => r.GetMostRentedAsync(Sessions.Admin.UserId, range.From, range.To, ReportPolicy.TopFilms, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        if (accepted)
        {
            Assert.Empty(await Service().GetMostRentedAsync(Sessions.Admin, range, Ct));
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().GetMostRentedAsync(Sessions.Admin, range, Ct));
            Assert.Equal(days < 0 ? ReportRangeValidator.Reversed : ReportRangeValidator.TooLong, error.Message);
        }
    }

    /// <summary>Days late and the fee so far follow the database's day, not the computer's.</summary>
    [Fact]
    public async Task Overdue_CountsTheDaysByTheDatabaseClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 5, 10, 0, 0, TimeSpan.Zero));
        _reports.Setup(r => r.GetOverdueAsync(Sessions.Admin.UserId, new DateOnly(2026, 3, 5), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OverdueRentalRow(7, "Film", "ornek", Today.AddDays(-5), Today.AddDays(-1))]);

        var row = Assert.Single(await Service(clock).GetOverdueAsync(Sessions.Admin, Ct));

        Assert.Equal((4, 30m), (row.OverdueDays, row.AccruedFee));
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task LateFees_AreSummedInDecimal()
    {
        _reports.Setup(r => r.GetLateFeesAsync(Sessions.Admin.UserId, March.From, March.To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Fee(1, 10.10m), Fee(2, 20.20m), Fee(3, 0.70m)]);

        var report = await Service().GetLateFeesAsync(Sessions.Admin, March, Ct);

        Assert.Equal(31.00m, report.Total);
    }

    [Fact]
    public async Task LateFees_NoneInThePeriod_IsAnEmptyReportWithZeroTotal()
    {
        _reports.Setup(r => r.GetLateFeesAsync(Sessions.Admin.UserId, March.From, March.To, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var file = await Service().ExportCsvAsync(Sessions.Admin, ReportKind.LateFees, March, Ct);

        Assert.Equal(
            "\"Kiralama No\";\"Film\";\"Kullanıcı\";\"Planlanan İade\";\"İade Tarihi\";\"Gecikme Ücreti\"\r\n\"\";\"\";\"\";\"\";\"Toplam\";\"0,00\"\r\n",
            Text(file.Content));
    }

    /// <summary>
    /// The CSV opens in a spreadsheet as text: UTF-8 with a byte order mark, ";" between quoted fields, and
    /// a leading "'" before anything a spreadsheet would run as a formula.
    /// </summary>
    [Fact]
    public async Task Csv_IsSpreadsheetSafe()
    {
        _reports.Setup(r => r.GetLateFeesAsync(Sessions.Admin.UserId, March.From, March.To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new LateFeeRow(1, "=HYPERLINK(\"http://kotu.example\")", "-ali", Today, Today.AddDays(2), 20m),
                new LateFeeRow(2, "@SUM(A1)", "+veli", Today, Today.AddDays(1), 10.5m),
                new LateFeeRow(3, "Şeker \"Tadında\"; Çay", "ayşe", Today, Today.AddDays(1), 10m),
            ]);

        var file = await Service().ExportCsvAsync(Sessions.Admin, ReportKind.LateFees, March, Ct);

        Assert.Equal([0xEF, 0xBB, 0xBF], file.Content[..3]);
        var lines = Text(file.Content).Split("\r\n");
        Assert.Equal("\"1\";\"'=HYPERLINK(\"\"http://kotu.example\"\")\";\"'-ali\";\"02.03.2026\";\"04.03.2026\";\"20,00\"", lines[1]);
        Assert.Equal("\"2\";\"'@SUM(A1)\";\"'+veli\";\"02.03.2026\";\"03.03.2026\";\"10,50\"", lines[2]);
        Assert.Equal("\"3\";\"Şeker \"\"Tadında\"\"; Çay\";\"ayşe\";\"02.03.2026\";\"03.03.2026\";\"10,00\"", lines[3]);
        Assert.Equal("\"\";\"\";\"\";\"\";\"Toplam\";\"40,50\"", lines[4]);
        Assert.Equal("gecikme-ucretleri-20260301-20260331.csv", file.SuggestedName);

        // Nothing is exported until the file is saved.
        Assert.Empty(_audit.Entries);
    }

    /// <summary>An export is recorded only when the screen reports the file saved, and only for an admin.</summary>
    [Fact]
    public void RecordExport_IsForAdminsAndWritesTheExportCode()
    {
        Assert.Throws<AccessDeniedException>(() => Service().RecordExport(Sessions.User));
        Service().RecordExport(Sessions.Admin);

        Assert.Equal(["access.denied user", "report.exported admin"], _audit.Entries);
    }

    /// <summary>
    /// The overdue export reads the database's day once: around midnight the file name has the day the
    /// rows were counted on.
    /// </summary>
    [Fact]
    public async Task Csv_Overdue_NamesTheFileWithTheDayItsRowsWereCountedOn()
    {
        var clock = new Mock<IClock>(MockBehavior.Strict);
        clock.SetupSequence(c => c.GetUtcNowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateTime(2026, 3, 5, 23, 59, 59, DateTimeKind.Utc))
            .ReturnsAsync(new DateTime(2026, 3, 6, 0, 0, 1, DateTimeKind.Utc));
        _reports.Setup(r => r.GetOverdueAsync(Sessions.Admin.UserId, new DateOnly(2026, 3, 5), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OverdueRentalRow(7, "Film", "ornek", Today.AddDays(-5), Today.AddDays(-1))]);
        var service = new ReportService(_reports.Object, clock.Object, _time, new RentalPolicy { DailyLateFee = 7.5m, MaxRentalDays = 30 }, _audit, new ReportRangeValidator());

        var file = await service.ExportCsvAsync(Sessions.Admin, ReportKind.Overdue, March, Ct);

        Assert.Equal("geciken-kiralamalar-20260305.csv", file.SuggestedName);
        Assert.Contains("\"4\";\"30,00\"", Text(file.Content), StringComparison.Ordinal);
        clock.Verify(c => c.GetUtcNowAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("\tsekme", "\"'\tsekme\"")]
    [InlineData("\rsatır", "\"'\rsatır\"")]
    [InlineData("a=1", "\"a=1\"")]
    [InlineData("", "\"\"")]
    [InlineData(null, "\"\"")]
    public void CsvField_PrefixesFormulaStartsOnly(string? value, string expected)
    {
        Assert.Equal(expected, ReportCsv.Field(value));
    }

    [Fact]
    public async Task Csv_MostRented_NamesTheReportAndPeriodOnly()
    {
        _reports.Setup(r => r.GetMostRentedAsync(Sessions.Admin.UserId, March.From, March.To, ReportPolicy.TopFilms, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RentalCountRow(4, "Sisli Liman", "Gerilim", 7), new RentalCountRow(2, "Son Vapur", "Dram", 3)]);

        var file = await Service().ExportCsvAsync(Sessions.Admin, ReportKind.MostRented, March, Ct);

        Assert.Equal("en-cok-kiralananlar-20260301-20260331.csv", file.SuggestedName);
        Assert.Equal(
            "\"Sıra\";\"Film\";\"Tür\";\"Kiralama Sayısı\"\r\n\"1\";\"Sisli Liman\";\"Gerilim\";\"7\"\r\n\"2\";\"Son Vapur\";\"Dram\";\"3\"\r\n",
            Text(file.Content));
    }

    [Fact]
    public async Task Recommendations_WithoutAnyRental_AreEmpty()
    {
        _reports.Setup(r => r.GetRentedGenresAsync(Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        Assert.Empty(await Recommendations().GetForMeAsync(Sessions.User, Ct));
        _reports.Verify(r => r.GetUnrentedAvailableAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Every genre tied for the most rentals is used; the best rated come first, unrated last, at most eight.</summary>
    [Fact]
    public async Task Recommendations_TakeTheMostRentedGenresAndOrderByRating()
    {
        IReadOnlyCollection<string>? asked = null;
        _reports.Setup(r => r.GetRentedGenresAsync(Sessions.User.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GenreCount("Dram", 3), new GenreCount("Komedi", 3), new GenreCount("Gerilim", 1)]);
        _reports.Setup(r => r.GetUnrentedAvailableAsync(Sessions.User.UserId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyCollection<string>, CancellationToken>((_, genres, _) => asked = genres)
            .ReturnsAsync(
            [
                Entry(1, "Zeta", 0, 0),
                Entry(2, "Alfa", 2, 6),   // 3.0
                Entry(3, "Beta", 1, 5),   // 5.0
                Entry(4, "Gama", 3, 15),  // 5.0, more ratings
                .. Enumerable.Range(10, 8).Select(i => Entry(i, "Dolgu " + i, 1, 4)),
            ]);

        var films = await Recommendations().GetForMeAsync(Sessions.User, Ct);

        Assert.Equal(["Dram", "Komedi"], asked!.Order(StringComparer.Ordinal));
        Assert.Equal(RecommendationPolicy.MaxFilms, films.Count);
        Assert.Equal([4, 3], films.Take(2).Select(f => f.Film.Id));
        Assert.DoesNotContain(films, f => f.Film.Id is 1 or 2);
    }

    private static CatalogEntry Entry(int id, string title, int count, int sum) =>
        new(new Film { Id = id, Title = title, Genre = "Dram", IsListed = true, AvailableCopies = 1 }, new RatingTotals(count, sum));

    private static LateFeeRow Fee(int id, decimal fee) => new(id, "Film", "ornek", Today, Today.AddDays(1), fee);

    private static string Text(byte[] content) => new UTF8Encoding(false).GetString(content.AsSpan(3));

    private ReportService Service(TimeProvider? clock = null) =>
        new(_reports.Object, new TimeProviderClock(clock ?? _time), _time, new RentalPolicy { DailyLateFee = 7.5m, MaxRentalDays = 30 }, _audit, new ReportRangeValidator());

    private RecommendationService Recommendations() => new(_reports.Object, _audit);
}
