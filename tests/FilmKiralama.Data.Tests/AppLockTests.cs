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
