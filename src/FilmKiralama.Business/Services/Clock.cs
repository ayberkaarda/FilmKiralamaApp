using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Services;

/// <summary>
/// The computer's clock as an <see cref="IClock"/>; used only when the data layer registers no clock of
/// its own (the application's data layer always registers the database's clock).
/// </summary>
internal sealed class TimeProviderClock(TimeProvider timeProvider) : IClock
{
    public Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(timeProvider.GetUtcNow().UtcDateTime);
}

internal static class ClockExtensions
{
    /// <summary>
    /// Today's local calendar day: the instant of <paramref name="clock"/> in the time zone of
    /// <paramref name="timeProvider"/>. Rental days are local calendar days.
    /// </summary>
    public static async Task<DateOnly> TodayAsync(this IClock clock, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var utc = DateTime.SpecifyKind(await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false), DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeProvider.LocalTimeZone));
    }
}
