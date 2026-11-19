using System.Globalization;
using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using FluentValidation;

namespace FilmKiralama.Business.Reports;

/// <summary>A period of local calendar days, both ends included.</summary>
public sealed record ReportRange(DateOnly From, DateOnly To);

/// <summary>The reports of the admin's report screen.</summary>
public enum ReportKind
{
    /// <summary>The films rented most often in a period.</summary>
    MostRented = 0,

    /// <summary>Rentals still out after their due day, today.</summary>
    Overdue = 1,

    /// <summary>Late fees of the rentals returned in a period, with their total.</summary>
    LateFees = 2,
}

/// <summary>An overdue rental with its days late and the fee it would cost if it came back today.</summary>
public sealed record OverdueReportRow(OverdueRentalRow Rental, int OverdueDays, decimal AccruedFee);

/// <summary>The late fees of a period and their total.</summary>
public sealed record LateFeeReport(IReadOnlyList<LateFeeRow> Rows, decimal Total);

/// <summary>Limits of the reports.</summary>
public static class ReportPolicy
{
    /// <summary>Films in the most rented report.</summary>
    public const int TopFilms = 20;

    /// <summary>Longest period a report covers.</summary>
    public const int MaxRangeDays = 3660;
}

/// <summary>
/// The admin's reports and their CSV export. Every report checks the admin's stored role. "Today" is the
/// database's day.
/// </summary>
public interface IReportService
{
    Task<IReadOnlyList<RentalCountRow>> GetMostRentedAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverdueReportRow>> GetOverdueAsync(UserSession session, CancellationToken cancellationToken = default);

    Task<LateFeeReport> GetLateFeesAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// The report <paramref name="kind"/> for <paramref name="range"/> (ignored for the overdue report) as
    /// CSV, with a file name made of the report and the dates only. Nothing is recorded: the screen calls
    /// <see cref="RecordExport"/> once the file is saved.
    /// </summary>
    Task<ExportFile> ExportCsvAsync(UserSession session, ReportKind kind, ReportRange range, CancellationToken cancellationToken = default);

    /// <summary>Records that an exported report was saved to a file (admin only).</summary>
    void RecordExport(UserSession session);
}

internal sealed class ReportService(
    IReportRepository reports,
    IClock clock,
    TimeProvider timeProvider,
    RentalPolicy policy,
    IAuditLog audit,
    IValidator<ReportRange> rangeValidator) : IReportService
{
    internal static readonly string[] MostRentedHeaders = ["Sıra", "Film", "Tür", "Kiralama Sayısı"];
    internal static readonly string[] OverdueHeaders = ["Kiralama No", "Film", "Kullanıcı", "Alış Tarihi", "İade Tarihi", "Gecikme (gün)", "Birikmiş Gecikme Ücreti"];
    internal static readonly string[] LateFeeHeaders = ["Kiralama No", "Film", "Kullanıcı", "Planlanan İade", "İade Tarihi", "Gecikme Ücreti"];

    public Task<IReadOnlyList<RentalCountRow>> GetMostRentedAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        Check(range);
        return audit.AsActorAsync(session, () => reports.GetMostRentedAsync(session.Actor, range.From, range.To, ReportPolicy.TopFilms, cancellationToken));
    }

    public async Task<IReadOnlyList<OverdueReportRow>> GetOverdueAsync(UserSession session, CancellationToken cancellationToken = default) =>
        (await GetOverdueOnAsync(session, cancellationToken).ConfigureAwait(false)).Rows;

    public async Task<LateFeeReport> GetLateFeesAsync(UserSession session, ReportRange range, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        Check(range);
        var rows = await audit.AsActorAsync(session, () => reports.GetLateFeesAsync(session.Actor, range.From, range.To, cancellationToken)).ConfigureAwait(false);
        return new LateFeeReport(rows, rows.Sum(r => r.LateFee));
    }

    public async Task<ExportFile> ExportCsvAsync(UserSession session, ReportKind kind, ReportRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        byte[] content;
        string name;
        switch (kind)
        {
            case ReportKind.MostRented:
                var counts = await GetMostRentedAsync(session, range, cancellationToken).ConfigureAwait(false);
                content = ReportCsv.Write(MostRentedHeaders, counts.Select((r, i) => (IReadOnlyList<string>)[Number(i + 1), r.Title, r.Genre, Number(r.Rentals)]));
                name = "en-cok-kiralananlar-" + Period(range);
                break;
            case ReportKind.Overdue:
                var (today, overdue) = await GetOverdueOnAsync(session, cancellationToken).ConfigureAwait(false);
                content = ReportCsv.Write(
                    OverdueHeaders,
                    overdue.Select(r => (IReadOnlyList<string>)
                    [
                        Number(r.Rental.RentalId), r.Rental.FilmTitle, r.Rental.UserName, Date(r.Rental.RentedOn), Date(r.Rental.DueOn), Number(r.OverdueDays), Money(r.AccruedFee),
                    ]));
                name = "geciken-kiralamalar-" + today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                break;
            case ReportKind.LateFees:
                var fees = await GetLateFeesAsync(session, range, cancellationToken).ConfigureAwait(false);
                content = ReportCsv.Write(
                    LateFeeHeaders,
                    fees.Rows
                        .Select(r => (IReadOnlyList<string>)[Number(r.RentalId), r.FilmTitle, r.UserName, Date(r.DueOn), Date(r.ReturnedOn), Money(r.LateFee)])
                        .Append(["", "", "", "", "Toplam", Money(fees.Total)]));
                name = "gecikme-ucretleri-" + Period(range);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown report.");
        }

        return new ExportFile(name + ".csv", content);
    }

    public void RecordExport(UserSession session)
    {
        Access.RequireAdmin(session, audit);
        audit.Record(AuditEvent.ReportExported, AuditRole.Admin);
    }

    /// <summary>The overdue rentals and the database's day they were counted on, read once.</summary>
    private async Task<(DateOnly Today, IReadOnlyList<OverdueReportRow> Rows)> GetOverdueOnAsync(UserSession session, CancellationToken cancellationToken)
    {
        Access.RequireAdmin(session, audit);
        var today = await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var rows = await audit.AsActorAsync(session, () => reports.GetOverdueAsync(session.Actor, today, cancellationToken)).ConfigureAwait(false);
        return (today,
        [
            .. rows.Select(r =>
            {
                var days = LateFeeCalculator.OverdueDays(r.DueOn, today);
                return new OverdueReportRow(r, days, LateFeeCalculator.LateFee(days, policy.DailyLateFee));
            }),
        ]);
    }

    private void Check(ReportRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        rangeValidator.ValidateOrThrow(range);
    }

    private static string Period(ReportRange range) =>
        range.From.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + range.To.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", InputRules.Turkish);
}
