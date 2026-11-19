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

    /// <summary>
    /// A film the session may see; an unlisted film is treated as missing unless the account is an admin
    /// in the database at the moment of the read.
    /// </summary>
    /// <exception cref="BusinessRuleException">The film does not exist (or is unlisted).</exception>
    Task<Film> GetAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);

    /// <summary>Every film with its stock (admin only).</summary>
    Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds a film with all its copies on the shelf (admin only) and returns its id.</summary>
    Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a film loaded at <paramref name="expectedVersion"/> (<see cref="Film.Version"/>), admin only;
    /// its copies cannot go below the copies rented out.
    /// </summary>
    /// <exception cref="BusinessRuleException">
    /// The input is invalid, or (with <see cref="BusinessRuleException.IsStale"/>) another admin changed or
    /// deleted the film since it was loaded.
    /// </exception>
    Task UpdateAsync(UserSession session, int filmId, int expectedVersion, FilmInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes a film that has never been rented (admin only).</summary>
    Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default);
}

internal sealed class FilmService(
    IFilmRepository films,
    TimeProvider timeProvider,
    IClock clock,
    IAuditLog audit,
    IValidator<FilmFields> validator,
    IValidator<CatalogQuery> queryValidator) : IFilmService
{
    internal const string NotFound = "Film bulunamadı.";
    internal const string InputInvalid = "Lütfen gerekli alanları doldurun.";
    internal const string HasRentals = "Kiralama geçmişi olan film silinemez. Kiralamaya kapatmak için \"Kiralamaya açık\" seçeneğini kaldırın.";
    internal const string CopiesBelowRented = "Adet, şu anda kirada olan kopya sayısından az olamaz.";
    internal const string ChangedElsewhere = "Film siz düzenlerken başka biri tarafından değiştirildi. Güncel hali yüklendi; lütfen değişikliğinizi yeniden yapın.";

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
        var page = await audit.AsActorAsync(session, () => films.SearchAsync(checkedQuery, session.Actor, asAdmin: false, cancellationToken)).ConfigureAwait(false);
        return Items(page);
    }

    public async Task<PagedResult<CatalogItem>> SearchAllAsync(UserSession session, CatalogQuery query, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var checkedQuery = Checked(query);
        var page = await audit.AsActorAsync(session, () => films.SearchAsync(checkedQuery, session.Actor, asAdmin: true, cancellationToken)).ConfigureAwait(false);
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

        // The repository decides with the stored role whether an unlisted film may be seen.
        return await audit.AsActorAsync(session, () => films.FindAsync(session.Actor, filmId, cancellationToken)).ConfigureAwait(false)
            ?? throw new BusinessRuleException(NotFound);
    }

    public Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        return audit.AsActorAsync(session, () => films.GetAllAsync(session.Actor, cancellationToken));
    }

    public async Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var film = Validate(input, change: false, await CurrentYearAsync(cancellationToken).ConfigureAwait(false));
        var id = await audit.AsActorAsync(session, () => films.AddAsync(session.Actor, film, cancellationToken)).ConfigureAwait(false);
        audit.Record(AuditEvent.FilmChanged, AuditRole.Admin);
        return id;
    }

    public async Task UpdateAsync(UserSession session, int filmId, int expectedVersion, FilmInput input, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var film = Validate(input, change: true, await CurrentYearAsync(cancellationToken).ConfigureAwait(false));
        film.Id = filmId;
        film.Version = expectedVersion;
        var outcome = await audit.AsActorAsync(session, () => films.UpdateAsync(session.Actor, film, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case FilmUpdateOutcome.Updated:
                audit.Record(AuditEvent.FilmChanged, AuditRole.Admin);
                return;
            case FilmUpdateOutcome.CopiesBelowRented:
                throw new BusinessRuleException(CopiesBelowRented);
            case FilmUpdateOutcome.ChangedElsewhere:
                throw BusinessRuleException.Stale(ChangedElsewhere);
            default:
                throw BusinessRuleException.Stale(NotFound);
        }
    }

    public async Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session, audit);
        var outcome = await audit.AsActorAsync(session, () => films.DeleteAsync(session.Actor, filmId, cancellationToken)).ConfigureAwait(false);
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

    /// <summary>
    /// The newest year a film may have: the current year by the database's clock, in this computer's time
    /// zone, so a computer whose own clock is wrong neither refuses this year's films nor accepts future ones.
    /// </summary>
    private async Task<int> CurrentYearAsync(CancellationToken cancellationToken) =>
        (await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false)).Year;

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
    private Film Validate(FilmInput input, bool change, int currentYear)
    {
        var fields = FilmFields.Create(input, currentYear, change);
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
