using FilmKiralama.Data.Persistence;

namespace FilmKiralama.Data.Tests;

/// <summary>sp_getapplock return codes; no database is needed.</summary>
public sealed class AppLockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EnsureGranted_AcceptsGrantedCodes(int code)
    {
        AppLock.EnsureGranted(code, "FilmKiralama.Film.1");
    }

    /// <summary>The lock command always outlasts the lock wait, so a held lock ends as "busy", not as a command timeout.</summary>
    [Theory]
    [InlineData(30_000, null, 45)]
    [InlineData(30_000, 5, 45)]
    [InlineData(30_000, 120, 120)]
    [InlineData(60_000, 30, 75)]
    [InlineData(60_000, 600, 600)]
    [InlineData(30_000, 0, 0)]
    public void CommandTimeoutFor_IsLongerThanTheLockWait(int lockTimeoutMilliseconds, int? configuredSeconds, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, AppLock.CommandTimeoutFor(lockTimeoutMilliseconds, configuredSeconds));
    }

    [Theory]
    [InlineData(-1, "timed out")]
    [InlineData(-2, "canceled")]
    [InlineData(-3, "deadlock")]
    [InlineData(-999, "invalid")]
    [InlineData(null, "no result")]
    public void EnsureGranted_RejectsFailureCodes(int? code, string reason)
    {
        var ex = Assert.Throws<AppLockNotGrantedException>(() => AppLock.EnsureGranted(code, "FilmKiralama.Film.1"));

        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
        Assert.Contains("No changes were made", ex.Message, StringComparison.Ordinal);
    }
}
