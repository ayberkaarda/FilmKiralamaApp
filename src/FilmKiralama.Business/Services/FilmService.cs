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

public interface IFilmService
{
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
    IValidator<FilmFields> validator) : IFilmService
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
