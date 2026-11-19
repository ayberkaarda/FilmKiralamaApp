using FilmKiralama.Business.Security;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

public sealed class SetupServiceTests
{
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateFirstAdmin_InvalidInput_NeverReachesTheRepository()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Baska.Sifre1", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CreateFirstAdminAsync(null, "kurucu", "kisa", "kisa", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CreateFirstAdminAsync(null, "", "Gizli.Sifre1", "Gizli.Sifre1", Ct));

        _users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateFirstAdmin_MayUseTheReservedName()
    {
        _users.Setup(u => u.AddFirstAdminAsync(It.Is<User>(a => a.UserName == "admin" && a.Role == UserRole.Admin), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirstAdminResult(FirstAdminStatus.Created, 1));

        var session = await Service().CreateFirstAdminAsync(null, "admin", "Gizli.Sifre1", "Gizli.Sifre1", Ct);

        Assert.True(session.IsAdmin);
        Assert.Equal(["setup.first_admin.created admin"], _audit.Entries);
    }

    [Theory]
    [InlineData(FirstAdminStatus.NotAuthorized, SetupService.SetupKeyInvalid)]
    [InlineData(FirstAdminStatus.KeyRequired, SetupService.SetupKeyRequired)]
    [InlineData(FirstAdminStatus.AdminExists, SetupService.AlreadyDone)]
    public async Task CreateFirstAdmin_Refusal_BecomesARuleMessage(FirstAdminStatus status, string message)
    {
        _users.Setup(u => u.AddFirstAdminAsync(It.IsAny<User>(), It.IsAny<SetupKeyAttempt?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirstAdminResult(status, 0));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().CreateFirstAdminAsync("ABCD", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));

        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("0123-4567-89ab-CDEF-0123-4567-89AB-cdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDE", false)]
    [InlineData("0123456789ABCDEF0123456789ABCDEG", false)]
    [InlineData("", false)]
    public void SetupKeyPolicy_AcceptsOnly32HexadecimalCharacters(string key, bool valid)
    {
        Assert.Equal(valid, SetupKeyPolicy.TryNormalize(key, out _));
    }

    private SetupService Service() => new(
        _users.Object,
        new PasswordHasher(iterations: 1_000),
        new SetupKeyGate(new FakeTimeProvider(), new SetupKeyThrottle(TimeSpan.Zero)),
        new SetupKeyEnvironment(null),
        _audit,
        TestValidators.FirstAdmin);
}
