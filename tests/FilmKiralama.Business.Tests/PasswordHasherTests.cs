using FilmKiralama.Business.Security;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Business.Tests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new(iterations: 1_000);

    [Fact]
    public void Hash_IsSaltedPbkdf2AndVerifies()
    {
        var first = _hasher.Hash("Gizli.Sifre1");
        var second = _hasher.Hash("Gizli.Sifre1");

        Assert.StartsWith("PBKDF2-SHA256$1000$", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Gizli.Sifre1", first, PasswordFormat.Pbkdf2));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("gizli.sifre1", first, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public void Verify_HashWithFewerIterations_AsksForARehash()
    {
        var weak = _hasher.Hash("Gizli.Sifre1");

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, new PasswordHasher(2_000).Verify("Gizli.Sifre1", weak, PasswordFormat.Pbkdf2));
    }

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("1234", "  1234 ")]
    [InlineData("1234   ", "1234")]
    public void Verify_LegacyPlainText_MatchesTheTrimmedInputAndAsksForARehash(string stored, string typed)
    {
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.Verify(typed, stored, PasswordFormat.LegacyPlainText));
    }

    [Theory]
    [InlineData("Sifre", "sifre")]
    [InlineData("1234", "12345")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Verify_LegacyPlainText_RefusesOtherTextCaseAndEmptyPasswords(string stored, string typed)
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify(typed, stored, PasswordFormat.LegacyPlainText));
    }

    [Fact]
    public void Verify_PlainTextThatLooksLikeAHash_IsNeverReadAsOne()
    {
        var hash = _hasher.Hash("Gizli.Sifre1");

        // The format of the account decides; a legacy password that happens to look like a hash is text.
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("Gizli.Sifre1", hash, PasswordFormat.LegacyPlainText));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("Gizli.Sifre1", "Gizli.Sifre1", PasswordFormat.Pbkdf2));
    }
}
