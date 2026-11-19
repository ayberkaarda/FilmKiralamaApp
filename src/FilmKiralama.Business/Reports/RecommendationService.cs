using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Repositories;

namespace FilmKiralama.Business.Reports;

/// <summary>Limits of the recommendations.</summary>
public static class RecommendationPolicy
{
    /// <summary>Films recommended at most.</summary>
    public const int MaxFilms = 8;
}

/// <summary>Simple recommendations from the customer's own rental history.</summary>
public interface IRecommendationService
{
    /// <summary>
    /// Films of the genre the account has rented most (every genre tied for the most), that it has never
    /// rented and that can be rented now: the best rated first (films without a rating last), then the
    /// most rated, then by title and id; at most <see cref="RecommendationPolicy.MaxFilms"/>. Empty for an
    /// account that has rented nothing.
    /// </summary>
    Task<IReadOnlyList<CatalogItem>> GetForMeAsync(UserSession session, CancellationToken cancellationToken = default);
}

internal sealed class RecommendationService(IReportRepository reports, IAuditLog audit) : IRecommendationService
{
    public async Task<IReadOnlyList<CatalogItem>> GetForMeAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var genres = await audit.AsActorAsync(session, () => reports.GetRentedGenresAsync(session.UserId, cancellationToken)).ConfigureAwait(false);
        if (genres.Count == 0)
        {
            return [];
        }

        var most = genres.Max(g => g.Rentals);
        var favourites = genres.Where(g => g.Rentals == most).Select(g => g.Genre).ToList();
        var candidates = await audit.AsActorAsync(session, () => reports.GetUnrentedAvailableAsync(session.UserId, favourites, cancellationToken)).ConfigureAwait(false);
        return
        [
            .. candidates
                .Select(CatalogItem.From)
                .OrderBy(i => i.AverageRating is null)
                .ThenByDescending(i => i.AverageRating)
                .ThenByDescending(i => i.RatingCount)
                .ThenBy(i => i.Film.Title, StringComparer.Ordinal)
                .ThenBy(i => i.Film.Id)
                .Take(RecommendationPolicy.MaxFilms),
        ];
    }
}
