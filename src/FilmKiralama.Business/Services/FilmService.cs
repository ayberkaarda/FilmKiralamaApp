using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;
using FilmKiralama.Domain.Repositories;
using FluentValidation;

namespace FilmKiralama.Business.Services;

/// <summary>A film as the admin's form enters it; texts as typed, checked by the service.</summary>
public sealed record FilmInput(
    string? Title,
    string? Genre,
    string? Year,
    string? Director,
    string? DailyPrice,
    string? ImageUrl,
    string? About,
    int Copies,
    bool IsListed);

/// <summary>A film of a search result with the average of its visible ratings (one decimal) and their number.</summary>
public sealed record CatalogItem(Film Film, decimal? AverageRating, int RatingCount)
{
    /// <summary>Listed with a copy on the shelf: it can be rented now.</summary>
    public bool IsAvailable => Film.IsListed && Film.AvailableCopies > 0;

    internal static CatalogItem From(CatalogEntry entry) =>
        new(entry.Film, Reviews.RatingMath.Average(entry.Rating), entry.Rating.Count);
}

public interface IFilmService
{
    /// <summary>
    /// One page of the customer's catalog (listed films only) for <paramref name="query"/>, ordered by
    /// title and id. A page beyond the last gives the last page.
    /// </summary>
    /// <exception cref="BusinessRuleException">The search is invalid (title, years, rating or page).</exception>
    Task<PagedResult<CatalogItem>> SearchCatalogAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default);

    /// <summary>One page of every film, unlisted ones included, for <paramref name="query"/> (admin only).</summary>
    Task<PagedResult<CatalogItem>> SearchAllAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default);

    /// <summary>Films a customer can rent now (listed, with a copy on the shelf) that match <paramref name="filter"/>.</summary>
    Task<IReadOnlyList<Film>> GetCatalogAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Genres, directors and years to filter by.</summary>
    Task<FilmFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>A film a customer may rent; an unlisted film is treated as missing for non-admins.</summary>
    /// <exception cref="BusinessRuleException">The film does not exist (or is unlisted).</exception>
    Task<Film> GetAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);

    /// <summary>Every film with its stock (admin only).</summary>
    Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds a film with all its copies on the shelf (admin only) and returns its id.</summary>
    Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default);

    /// <summary>Changes a film (admin only); its copies cannot go below the copies rented out.</summary>
    Task UpdateAsync(UserSession session, int filmId, FilmInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes a film that has never been rented (admin only).</summary>
    Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);
}

internal sealed class FilmService(
    IFilmRepository films,
    TimeProvider timeProvider,
    IAuditLog audit,
    IValidator<FilmFields> validator,
    IValidator<CatalogQuery> queryValidator) : IFilmService
{
    internal const string NotFound = "Film bulunamadı.";
    internal const string InputInvalid = "Lütfen gerekli alanları doldurun.";
    internal const string HasRentals = "Kiralama geçmişi olan film silinemez. Kiralamaya kapatmak için \"Kiralamaya açık\" seçeneğini kaldırın.";
    internal const string CopiesBelowRented = "Adet, şu anda kirada olan kopya sayısından az olamaz.";

    public Task<IReadOnlyList<Film>> GetCatalogAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        ArgumentNullException.ThrowIfNull(filter);
        return films.GetCatalogAsync(filter, cancellationToken);
    }

    public async Task<PagedResult<CatalogItem>> SearchCatalogAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var checkedQuery = Checked(query);
        var page = await audit.AsActorAsync(session, () => films.SearchAsync(checkedQuery, session.UserId, asAdmin: false, cancellationToken)).ConfigureAwait(false);
        return Items(page);
    }

    public async Task<PagedResult<CatalogItem>> SearchAllAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var checkedQuery = Checked(query);
        var page = await audit.AsActorAsync(session, () => films.SearchAsync(checkedQuery, session.UserId, asAdmin: true, cancellationToken)).ConfigureAwait(false);
        return Items(page);
    }

    public Task<FilmFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return films.GetFilterOptionsAsync(cancellationToken);
    }

    public async Task<Film> GetAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        var film = await films.FindAsync(filmId, cancellationToken).ConfigureAwait(false);
        if (film is null || (!film.IsListed && !session.IsAdmin))
        {
            throw new BusinessRuleException(NotFound);
        }

        return film;
    }

    public Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        return audit.AsActorAsync(session, () => films.GetAllAsync(session.UserId, cancellationToken));
    }

    public async Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var film = Validate(input, change: false);
        var id = await audit.AsActorAsync(session, () => films.AddAsync(session.UserId, film, cancellationToken)).ConfigureAwait(false);
        audit.Record(AuditEvent.FilmChanged, AuditRole.Admin);
        return id;
    }

    public async Task UpdateAsync(UserSession session, int filmId, FilmInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var film = Validate(input, change: true);
        film.Id = filmId;
        var outcome = await audit.AsActorAsync(session, () => films.UpdateAsync(session.UserId, film, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case FilmUpdateOutcome.Updated:
                audit.Record(AuditEvent.FilmChanged, AuditRole.Admin);
                return;
            case FilmUpdateOutcome.CopiesBelowRented:
                throw new BusinessRuleException(CopiesBelowRented);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    public async Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var outcome = await audit.AsActorAsync(session, () => films.DeleteAsync(session.UserId, filmId, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case FilmDeleteOutcome.Deleted:
                audit.Record(AuditEvent.FilmChanged, AuditRole.Admin);
                return;
            case FilmDeleteOutcome.HasRentals:
                throw new BusinessRuleException(HasRentals);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    /// <summary>The query with its title trimmed (an empty one searches nothing), refused when invalid.</summary>
    private CatalogQuery Checked(CatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var title = string.IsNullOrWhiteSpace(query.Title) ? null : query.Title.Trim();
        var genre = string.IsNullOrWhiteSpace(query.Genre) ? null : query.Genre;
        var normalized = query with { Title = title, Genre = genre };
        queryValidator.ValidateOrThrow(normalized);
        return normalized;
    }

    private static PagedResult<CatalogItem> Items(PagedResult<CatalogEntry> page) =>
        new([.. page.Items.Select(CatalogItem.From)], page.TotalCount, page.Page, page.PageSize);

    /// <summary>
    /// Checks every field of <paramref name="input"/> and collects one message per invalid field, as the
    /// original form marked each empty field; nothing is stored unless all are valid. On a
    /// <paramref name="change"/> the description may be empty (stored as NULL): the upgrade keeps films
    /// of the original application without one, and they can be changed or listed again as they are.
    /// </summary>
    private Film Validate(FilmInput input, bool change)
    {
        var fields = FilmFields.Create(input, timeProvider.Today().Year, change);
        var result = validator.Validate(fields);
        if (!result.IsValid)
        {
            // One message per field (each rule stops at its first failure), in the order of the form.
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var failure in result.Errors)
            {
                errors.TryAdd(failure.PropertyName, failure.ErrorMessage);
            }

            throw new BusinessRuleException(InputInvalid + "\n\n" + string.Join("\n", errors.Values), errors);
        }

        var price = InputRules.TryParseMoney(fields.DailyPrice, out var parsed) ? parsed : 0m;
        return new Film
        {
            Title = fields.Title!.Trim(),
            Genre = fields.Genre!.Trim(),
            Year = FilmFieldsValidator.ParseYear(fields.Year)!.Value,
            Director = fields.Director!.Trim(),
            DailyPrice = price,
            ImageUrl = string.IsNullOrWhiteSpace(fields.ImageUrl) ? null : fields.ImageUrl.Trim(),
            About = string.IsNullOrWhiteSpace(fields.About) ? null : fields.About.Trim(),
            TotalCopies = fields.Copies,
            AvailableCopies = fields.Copies,
            IsListed = input.IsListed,
        };
    }
}
