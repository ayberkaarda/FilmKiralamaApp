using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FilmKiralama.Business.Tests;

/// <summary>Ratings, comments and their moderation with strict mocks: refused input never reaches a repository.</summary>
public sealed class ReviewServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IReviewRepository> _reviews = new(MockBehavior.Strict);
    private readonly Mock<IFilmRepository> _films = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Save_RatingOutsideOneToFive_IsRefusedBeforeTheRepository(int rating)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SaveAsync(Sessions.User, 3, new ReviewInput(rating, null), Ct));

        Assert.Equal(ReviewFieldsValidator.RatingInvalid, error.Message);
        _reviews.VerifyNoOtherCalls();
        Assert.Empty(_audit.Entries);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Save_RatingAtTheLimits_IsStored(int rating)
    {
        _reviews.Setup(r => r.SaveAsync(Sessions.User.Actor, 3, rating, null, Now, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewSaveOutcome.Created);

        await Service().SaveAsync(Sessions.User, 3, new ReviewInput(rating, "   "), Ct);

        Assert.Equal(["review.saved user"], _audit.Entries);
    }

    [Fact]
    public async Task Save_CommentOfTheLongestLength_IsStoredTrimmedAndOneMoreIsRefused()
    {
        var longest = new string('a', ReviewPolicy.MaxCommentLength);
        _reviews.Setup(r => r.SaveAsync(Sessions.User.Actor, 3, 4, longest, Now, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewSaveOutcome.Updated);

        await Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, "  " + longest + "  "), Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, longest + "b"), Ct));

        Assert.Equal(ReviewFieldsValidator.CommentTooLong, error.Message);
        Assert.Equal("Yorum en fazla 500 karakter olabilir.", error.Message);
    }

    /// <summary>Markup is plain text: it is stored as typed and shown as typed, never interpreted.</summary>
    [Fact]
    public async Task Save_CommentWithMarkupAndFormulaCharacters_IsStoredAsTyped()
    {
        const string Comment = "<b>Harika</b> & <script>alert(1)</script> =1+1 😀";
        _reviews.Setup(r => r.SaveAsync(Sessions.User.Actor, 3, 5, Comment, Now, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewSaveOutcome.Created);

        await Service().SaveAsync(Sessions.User, 3, new ReviewInput(5, Comment), Ct);

        _reviews.VerifyAll();
    }

    [Theory]
    [InlineData(0x0A)]   // line feed
    [InlineData(0x09)]   // tab
    [InlineData(0x202E)] // right-to-left override
    [InlineData(0x200B)] // zero-width space
    [InlineData(0x2028)] // line separator
    [InlineData(0xE000)] // private use
    public async Task Save_CommentWithControlOrInvisibleCharacters_IsRefused(int codePoint)
    {
        var comment = "yorum " + char.ConvertFromUtf32(codePoint) + " metni";

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, comment), Ct));

        Assert.Equal(ReviewFieldsValidator.CommentInvalid, error.Message);
        _reviews.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Save_CommentWithABrokenSurrogatePair_IsRefused()
    {
        var comment = "yarım " + (char)0xD800 + " vekil";

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, comment), Ct));

        Assert.Equal(ReviewFieldsValidator.CommentInvalid, error.Message);
    }

    [Fact]
    public async Task Save_FilmNeverReturned_IsRefusedAndNotRecorded()
    {
        _reviews.Setup(r => r.SaveAsync(Sessions.User.Actor, 3, 4, null, Now, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewSaveOutcome.NotEligible);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, null), Ct));

        Assert.Equal(ReviewService.NotEligible, error.Message);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>A new review and a change of one leave the same entry, so the log does not tell them apart.</summary>
    [Fact]
    public async Task Save_NewAndChangedReview_ShareOneAuditCode()
    {
        _reviews.SetupSequence(r => r.SaveAsync(Sessions.User.Actor, 3, 4, "iyi", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReviewSaveOutcome.Created)
            .ReturnsAsync(ReviewSaveOutcome.Updated);

        await Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, "iyi"), Ct);
        await Service().SaveAsync(Sessions.User, 3, new ReviewInput(4, "iyi"), Ct);

        Assert.Equal(["review.saved user", "review.saved user"], _audit.Entries);
    }

    [Fact]
    public async Task DeleteOwn_MissingOrForeignReview_GetsOneAnswerAndNoEntry()
    {
        _reviews.Setup(r => r.DeleteOwnAsync(Sessions.User.Actor, 7, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteOwnAsync(Sessions.User, 7, Ct));

        Assert.Equal(ReviewService.ReviewNotFound, error.Message);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task FilmReviews_AverageIsRoundedAndAuthorsAreMasked()
    {
        _films.Setup(f => f.FindAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new Film { Id = 3, IsListed = true });
        _reviews.Setup(r => r.GetVisibleAsync(Sessions.User.Actor, 3, ReviewPolicy.ShownWithFilm, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([new ReviewDetails(1, 3, "Film", 9, "ılgaz.deniz", 4, "Güzel", false, Now.AddDays(-1))], new RatingTotals(3, 11)));

        var result = await Service().GetFilmReviewsAsync(Sessions.User, 3, Ct);

        Assert.Equal(3.7m, result.Average);
        Assert.Equal(3, result.Count);
        var review = Assert.Single(result.Reviews);
        Assert.Equal("I***", review.Author);
        Assert.DoesNotContain("deniz", review.Author, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 3, 1), review.Date);
    }

    [Fact]
    public async Task FilmReviews_OfAnUnlistedFilm_AreMissingForCustomers()
    {
        _films.Setup(f => f.FindAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new Film { Id = 3, IsListed = false });

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().GetFilmReviewsAsync(Sessions.User, 3, Ct));

        Assert.Equal(ReviewService.FilmNotFound, error.Message);
        _reviews.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Moderation_IsForAdminsOnly()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().SetHiddenAsync(Sessions.User, 1, true, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service().GetAllAsync(Sessions.User, false, 1, Ct));

        _reviews.VerifyNoOtherCalls();
        Assert.Equal(["access.denied user", "access.denied user"], _audit.Entries);
    }

    [Fact]
    public async Task Moderation_MissingReview_IsReportedAndAnExistingOneRecorded()
    {
        _reviews.Setup(r => r.SetHiddenAsync(Sessions.Admin.Actor, 7, true, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _reviews.Setup(r => r.SetHiddenAsync(Sessions.Admin.Actor, 8, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().SetHiddenAsync(Sessions.Admin, 7, true, Ct));
        await Service().SetHiddenAsync(Sessions.Admin, 8, true, Ct);

        Assert.Equal(ReviewService.ReviewNotFound, error.Message);
        Assert.Equal(["review.moderated admin"], _audit.Entries);
    }

    [Fact]
    public async Task Moderation_ByARevokedAdmin_AsksToSignInAgain()
    {
        _reviews.Setup(r => r.SetHiddenAsync(Sessions.Admin.Actor, 8, true, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());

        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => Service().SetHiddenAsync(Sessions.Admin, 8, true, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        Assert.Equal(["access.session_revoked admin"], _audit.Entries);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 5, 5.0)]
    [InlineData(2, 7, 3.5)]
    [InlineData(3, 11, 3.7)]   // 3.666...
    [InlineData(3, 2 * 3, 2.0)]
    [InlineData(4, 13, 3.3)]   // 3.25: halves away from zero (banker's rounding would give 3.2)
    [InlineData(20, 79, 4.0)]  // 3.95
    [InlineData(20, 78, 3.9)]
    [InlineData(6, 7, 1.2)]    // 1.1666...
    public void Average_IsRoundedToOneDecimalAwayFromZero(int count, int sum, double? expected)
    {
        Assert.Equal(expected is null ? null : (decimal)expected.Value, RatingMath.Average(new RatingTotals(count, sum)));
    }

    /// <summary>The catalog filter's integer condition in the database gives the same answer as the shown average.</summary>
    [Fact]
    public void MeetsMinimum_MatchesTheDatabaseConditionForEveryTotal()
    {
        for (var count = 0; count <= 40; count++)
        {
            for (var sum = count; sum <= count * 5; sum++)
            {
                for (var minimum = 1; minimum <= 5; minimum++)
                {
                    var totals = new RatingTotals(count, sum);
                    var database = count > 0 && 20 * sum >= ((20 * minimum) - 1) * count;
                    Assert.Equal(database, RatingMath.MeetsMinimum(totals, minimum));
                }
            }
        }
    }

    [Fact]
    public void Format_ShowsOneDecimalTheTurkishWay()
    {
        Assert.Equal("3,7", RatingMath.Format(3.7m));
        Assert.Equal("-", RatingMath.Format(null));
    }

    private ReviewService Service() => new(_reviews.Object, _films.Object, new TimeProviderClock(_time), _time, _audit, new ReviewFieldsValidator());
}
