using FilmKiralama.Business;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.UI.Tests.Support;

/// <summary>Sign-in, sign-up and password calls that stay pending until the test completes them.</summary>
internal sealed class PendingAuthService : IAuthService
{
    private TaskCompletionSource<UserSession> _signIn = new();
    private TaskCompletionSource _register = new();

    public int SignInCalls { get; private set; }

    public int RegisterCalls { get; private set; }

    public string? LastPassword { get; private set; }

    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        SignInCalls++;
        LastPassword = password;
        _signIn = new TaskCompletionSource<UserSession>();
        return _signIn.Task;
    }

    public Task RegisterAsync(string? userName, string? password, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        RegisterCalls++;
        _register = new TaskCompletionSource();
        return _register.Task;
    }

    public Task ChangePasswordAsync(UserSession session, string? currentPassword, string? newPassword, string? newPasswordRepeat, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RefuseSignIn() => _signIn.SetException(new BusinessRuleException("Kullanıcı adı veya şifre hatalı."));

    public void CompleteSignIn(UserSession session) => _signIn.SetResult(session);

    public void CompleteRegister() => _register.SetResult();
}

/// <summary>Film calls answered by the test: catalog requests in any order, writes when it says so.</summary>
internal sealed class ControlledFilmService : IFilmService
{
    private TaskCompletionSource<int> _add = new();

    public List<(FilmFilter Filter, TaskCompletionSource<IReadOnlyList<Film>> Answer)> CatalogRequests { get; } = [];

    public List<Film> Films { get; } = [Film(1, "Sisli Liman", "Gerilim"), Film(2, "Son Vapur", "Dram")];

    public FilmFilterOptions Options { get; set; } = new(["Dram", "Gerilim"], ["Ada Örnek"], [2019, 2015]);

    public int AddCalls { get; private set; }

    /// <summary>Thrown by the next add instead of keeping it pending.</summary>
    public Exception? AddFailure { get; set; }

    public static Film Film(int id, string title, string genre) => new()
    {
        Id = id,
        Title = title,
        Genre = genre,
        Year = 2019,
        Director = "Ada Örnek",
        DailyPrice = 12.5m,
        TotalCopies = 2,
        AvailableCopies = 1,
        IsListed = true,
    };

    public Task<IReadOnlyList<Film>> GetCatalogAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<IReadOnlyList<Film>>();
        CatalogRequests.Add((filter, answer));
        return answer.Task;
    }

    public Task<FilmFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default) => Task.FromResult(Options);

    public Task<Film> GetAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Films.Single(f => f.Id == filmId));

    public Task<IReadOnlyList<Film>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Film>>(Films);

    public Task<int> AddAsync(UserSession session, FilmInput input, CancellationToken cancellationToken = default)
    {
        AddCalls++;
        if (AddFailure is { } failure)
        {
            return Task.FromException<int>(failure);
        }

        _add = new TaskCompletionSource<int>();
        return _add.Task;
    }

    public Task UpdateAsync(UserSession session, int filmId, FilmInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void CompleteAdd() => _add.SetResult(3);
}

/// <summary>Rental calls; rentals and returns stay pending until the test completes them.</summary>
internal sealed class ControlledRentalService : IRentalService
{
    private TaskCompletionSource<RentalReceipt> _rent = new();
    private TaskCompletionSource<ReturnReceipt> _return = new();

    public RentalPolicy Policy { get; } = new() { DailyLateFee = 10m, MaxRentalDays = 30 };

    public List<RentalDetails> Rentals { get; } = [Rental(9), Rental(10)];

    public int RentCalls { get; private set; }

    public int ReturnCalls { get; private set; }

    public RentalQuote? LastQuote { get; private set; }

    public static RentalDetails Rental(int id) =>
        new(id, 1, "Sisli Liman", "Gerilim", 2019, "Ada Örnek", null, 2, "ornek", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4), 37.5m, RentalStatus.Active);

    public RentalQuote Quote(int filmId, decimal dailyPrice, int days) =>
        new(filmId, days, dailyPrice, dailyPrice * days, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2).AddDays(days));

    public Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default)
    {
        RentCalls++;
        LastQuote = quote;
        _rent = new TaskCompletionSource<RentalReceipt>();
        return _rent.Task;
    }

    public Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals]);

    public Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, FilmFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals]);

    public ReturnPreview PreviewReturn(RentalDetails rental) => new(0, 0m);

    public Task<ReturnReceipt> ReturnAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default)
    {
        ReturnCalls++;
        _return = new TaskCompletionSource<ReturnReceipt>();
        return _return.Task;
    }

    public void CompleteRent() => _rent.SetResult(new RentalReceipt(11, LastQuote!.Total, LastQuote.StartsOn, LastQuote.DueOn));

    public void CompleteReturn() => _return.SetResult(new ReturnReceipt(0, 0m));
}

/// <summary>Review calls; reviews of a film come from <see cref="FilmReviews"/>, writes stay pending until the test completes them.</summary>
internal sealed class ControlledReviewService : IReviewService
{
    private TaskCompletionSource _save = new();
    private TaskCompletionSource _hide = new();

    public FilmReviews FilmReviews { get; set; } = new(null, 0, []);

    public List<ReviewableFilm> Reviewable { get; } = [new(1, "Sisli Liman", "Gerilim", 2019, null, null), new(2, "Son Vapur", "Dram", 2015, 7, 4)];

    public OwnReview? Own { get; set; }

    public List<(bool HiddenOnly, int Page, TaskCompletionSource<PagedResult<ReviewDetails>> Answer)> ListRequests { get; } = [];

    public List<ReviewInput> Saved { get; } = [];

    public int HideCalls { get; private set; }

    public static ReviewDetails Review(int id, bool hidden = false) =>
        new(id, 1, "Sisli Liman", 2, "ornek", 4, "Yorum " + id, hidden, new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));

    public Task SaveAsync(UserSession session, int filmId, ReviewInput input, CancellationToken cancellationToken = default)
    {
        Saved.Add(input);
        _save = new TaskCompletionSource();
        return _save.Task;
    }

    public Task<OwnReview?> GetOwnAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) => Task.FromResult(Own);

    public Task DeleteOwnAsync(UserSession session, int reviewId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FilmReviews> GetFilmReviewsAsync(UserSession session, int filmId, CancellationToken cancellationToken = default) => Task.FromResult(FilmReviews);

    public Task<IReadOnlyList<ReviewableFilm>> GetReviewableAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReviewableFilm>>([.. Reviewable]);

    public Task<PagedResult<ReviewDetails>> GetAllAsync(UserSession session, bool hiddenOnly, int page, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<PagedResult<ReviewDetails>>();
        ListRequests.Add((hiddenOnly, page, answer));
        return answer.Task;
    }

    public Task SetHiddenAsync(UserSession session, int reviewId, bool hidden, CancellationToken cancellationToken = default)
    {
        HideCalls++;
        _hide = new TaskCompletionSource();
        return _hide.Task;
    }

    public void CompleteSave() => _save.SetResult();

    public void CompleteHide() => _hide.SetResult();
}

/// <summary>Answers confirmations with yes and records every message instead of showing a dialog.</summary>
internal sealed class RecordingPrompts : IUserPrompts
{
    public List<string> Messages { get; } = [];

    public bool Confirm(string question)
    {
        Messages.Add(question);
        return true;
    }

    public void Inform(string message) => Messages.Add(message);

    public void Warn(string message) => Messages.Add(message);
}

internal sealed class NoFormsFactory : IFormFactory
{
    public T Create<T>(params object[] arguments)
        where T : Form => throw new InvalidOperationException("Tests do not open other forms.");
}

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin);
}
