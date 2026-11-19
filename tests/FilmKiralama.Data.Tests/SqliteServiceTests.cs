using FilmKiralama.Business;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Business.Services;
using FilmKiralama.Data.Persistence;
using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static FilmKiralama.Data.Tests.TestServices;

namespace FilmKiralama.Data.Tests;

/// <summary>The real services and repositories on SQLite: rental rules, returns, ownership and admin rules.</summary>
public sealed class SqliteServiceTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Rent_TakesOneCopyOffTheShelfAndStoresTheDates()
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId, days: 4);

        await using var db = _db.CreateContext();
        var film = await db.Films.SingleAsync(f => f.Id == ThreeCopiesFilmId, Ct);
        var rental = await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct);
        Assert.Equal(2, film.AvailableCopies);
        Assert.Equal(3, film.TotalCopies);
        Assert.Equal(RentalStatus.Active, rental.Status);
        Assert.Equal(Today, rental.RentedOn);
        Assert.Equal(Today.AddDays(4), rental.DueOn);
        Assert.Null(rental.ReturnedOn);
        Assert.Equal(100m, rental.Price);
    }

    [Fact]
    public async Task Rent_SameFilmTwice_IsRefusedUntilItIsReturned()
    {
        var first = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId));
        Assert.Equal(RentalService.AlreadyRented, error.Message);

        await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), first.RentalId, Ct);
        await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        Assert.Equal(2, await AvailableAsync(ThreeCopiesFilmId));
    }

    [Fact]
    public async Task Rent_LastCopy_IsRefusedForTheNextCustomer()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(_db.Services, other, SingleCopyFilmId));

        Assert.Equal(RentalService.NotAvailable, error.Message);
        Assert.Equal(0, await AvailableAsync(SingleCopyFilmId));
        Assert.DoesNotContain(await _db.Get<IFilmService>().GetCatalogAsync(other, FilmFilter.None, Ct), f => f.Id == SingleCopyFilmId);
    }

    [Fact]
    public async Task Rent_PriceChangedAfterTheQuote_IsRefusedAndTakesNoCopy()
    {
        var rentals = _db.Get<IRentalService>();
        var quote = await rentals.QuoteAsync(ThreeCopiesFilmId, 25m, 2, Ct);
        await UpdateFilmAsync(ThreeCopiesFilmId, price: "27,50");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.RentAsync(User(SeedUserId), quote, Ct));

        Assert.Equal(RentalService.PriceChanged, error.Message);
        Assert.Equal(3, await AvailableAsync(ThreeCopiesFilmId));
    }

    [Fact]
    public async Task Rent_UnlistedFilm_IsNotAvailable()
    {
        await UpdateFilmAsync(ThreeCopiesFilmId, listed: false);

        var rentals = _db.Get<IRentalService>();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(async () => await rentals.RentAsync(User(SeedUserId), await rentals.QuoteAsync(ThreeCopiesFilmId, 25m, 1, Ct), Ct));

        Assert.Equal(RentalService.NotAvailable, error.Message);
    }

    [Fact]
    public async Task Return_ThreeDaysLate_StoresTheLateFeeAndPutsTheCopyBack()
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId, days: 2);
        _db.Clock.Advance(TimeSpan.FromDays(5));

        var returned = await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);

        Assert.Equal(3, returned.OverdueDays);
        Assert.Equal(3 * DailyLateFee, returned.LateFee);
        await using var db = _db.CreateContext();
        var rental = await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct);
        Assert.Equal(RentalStatus.Returned, rental.Status);
        Assert.Equal(Today.AddDays(5), rental.ReturnedOn);
        Assert.Equal(30m, rental.LateFee);
        Assert.Equal(1, await AvailableAsync(SingleCopyFilmId));
    }

    [Fact]
    public async Task Return_OnTime_StoresNoLateFee()
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId, days: 2);
        _db.Clock.Advance(TimeSpan.FromDays(2));

        var returned = await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);

        Assert.Equal(0m, returned.LateFee);
        await using var db = _db.CreateContext();
        Assert.Null((await db.Rentals.SingleAsync(r => r.Id == receipt.RentalId, Ct)).LateFee);
    }

    /// <summary>IDOR: another account's rental cannot be seen or returned, and the answer is that of a missing one.</summary>
    [Fact]
    public async Task Return_OtherAccountsRental_GetsTheSameAnswerAsAMissingOneAndChangesNothing()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var receipt = await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId);
        var rentals = _db.Get<IRentalService>();

        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.ReturnAsync(other, receipt.RentalId, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.ReturnAsync(other, 999, Ct));

        Assert.Equal(missing.Message, foreign.Message);
        Assert.Empty(await rentals.GetMyActiveRentalsAsync(other, Ct));
        Assert.Single(await rentals.GetMyActiveRentalsAsync(User(SeedUserId), Ct));
        Assert.Equal(0, await AvailableAsync(SingleCopyFilmId));

        // An admin may take any rental back.
        await rentals.ReturnAsync(Admin(SeedAdminId), receipt.RentalId, Ct);
        Assert.Equal(1, await AvailableAsync(SingleCopyFilmId));
    }

    [Fact]
    public async Task Return_Twice_TheSecondIsRefusedAndTheShelfStaysRight()
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        var rentals = _db.Get<IRentalService>();
        await rentals.ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() => rentals.ReturnAsync(User(SeedUserId), receipt.RentalId, Ct));

        Assert.Equal(3, await AvailableAsync(ThreeCopiesFilmId));
    }

    [Fact]
    public async Task ActiveRentals_AdminSeesEveryoneAndCanFilterByFilm()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        await RentAsync(_db.Services, other, SingleCopyFilmId);
        var rentals = _db.Get<IRentalService>();

        Assert.Equal(2, (await rentals.GetActiveRentalsAsync(Admin(SeedAdminId), FilmFilter.None, Ct)).Count);
        var thrillers = await rentals.GetActiveRentalsAsync(Admin(SeedAdminId), new FilmFilter(Genre: "Gerilim"), Ct);
        Assert.Equal(ThreeCopiesFilmId, Assert.Single(thrillers).FilmId);
        var byYear = await rentals.GetActiveRentalsAsync(Admin(SeedAdminId), new FilmFilter(Year: 2021), Ct);
        Assert.Equal(SingleCopyFilmId, Assert.Single(byYear).FilmId);
    }

    [Fact]
    public async Task Catalog_FilterOptionsAndFilters_MatchTheFilms()
    {
        var films = _db.Get<IFilmService>();

        var options = await films.GetFilterOptionsAsync(User(SeedUserId), Ct);
        var byDirector = await films.GetCatalogAsync(User(SeedUserId), new FilmFilter(Director: "Ada Örnek"), Ct);

        Assert.Contains("Gerilim", options.Genres);
        Assert.Equal(2023, options.Years[0]);
        Assert.Equal(2, byDirector.Count);
    }

    [Fact]
    public async Task UpdateFilm_CopiesFollowTheRentedOnes()
    {
        await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        var other = await RegisterAsync(_db.Services, "baska", 1);
        await RentAsync(_db.Services, other, ThreeCopiesFilmId);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => UpdateFilmAsync(ThreeCopiesFilmId, copies: 1));
        Assert.Equal(FilmService.CopiesBelowRented, error.Message);

        await UpdateFilmAsync(ThreeCopiesFilmId, copies: 5);
        Assert.Equal(3, await AvailableAsync(ThreeCopiesFilmId));
        await UpdateFilmAsync(ThreeCopiesFilmId, copies: 2);
        Assert.Equal(0, await AvailableAsync(ThreeCopiesFilmId));
    }

    [Fact]
    public async Task DeleteFilm_WithRentalHistory_IsRefusedAndWithoutIsDone()
    {
        var receipt = await RentAsync(_db.Services, User(SeedUserId), SingleCopyFilmId);
        await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), receipt.RentalId, Ct);
        var films = _db.Get<IFilmService>();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => films.DeleteAsync(Admin(SeedAdminId), SingleCopyFilmId, Ct));
        Assert.Equal(FilmService.HasRentals, error.Message);

        var id = await AddFilmAsync(_db.Services, "Silinecek", 1);
        await films.DeleteAsync(Admin(SeedAdminId), id, Ct);
        await using var db = _db.CreateContext();
        Assert.False(await db.Films.AnyAsync(f => f.Id == id, Ct));
    }

    [Fact]
    public async Task DeleteAccount_AdminOrWithRentals_IsRefused()
    {
        var accounts = _db.Get<IAccountService>();
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var unused = await RegisterAsync(_db.Services, "bos", 2);
        await RentAsync(_db.Services, other, SingleCopyFilmId);

        Assert.Equal(AccountService.AdminNotDeletable, (await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.DeleteAsync(Admin(SeedAdminId), SeedAdminId, Ct))).Message);
        Assert.Equal(AccountService.HasRentals, (await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.DeleteAsync(Admin(SeedAdminId), other.UserId, Ct))).Message);

        await accounts.DeleteAsync(Admin(SeedAdminId), unused.UserId, Ct);
        Assert.DoesNotContain(await accounts.GetAllAsync(Admin(SeedAdminId), Ct), u => u.Id == unused.UserId);
    }

    [Fact]
    public async Task UpdateAccount_LastAdminKeepsTheRoleButASecondAdminCanBeDemoted()
    {
        var accounts = _db.Get<IAccountService>();
        var demote = new AccountInput("yonetici", null, "yonetici@example.com", "5550000001", UserRole.User);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => accounts.UpdateLoadedAsync(Admin(SeedAdminId), SeedAdminId, demote, Ct));
        Assert.Equal(AccountService.LastAdmin, error.Message);

        var second = await accounts.AddAsync(Admin(SeedAdminId), new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin), Ct);
        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), SeedAdminId, demote, Ct);

        var all = await accounts.GetAllAsync(Admin(second), Ct);
        Assert.Equal(UserRole.User, all.Single(u => u.Id == SeedAdminId).Role);
    }

    /// <summary>
    /// Admin A opened an admin's account; admin B demoted it meanwhile. A's save of an e-mail change, with
    /// the admin role the old form still shows, is refused as stale and gives the role back to nobody. A
    /// change made after loading the account again goes through.
    /// </summary>
    [Fact]
    public async Task UpdateAccount_FromAFormLoadedBeforeADemotion_IsRefused()
    {
        var accounts = _db.Get<IAccountService>();
        var input = new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin);
        var second = await accounts.AddAsync(Admin(SeedAdminId), input, Ct);
        var loadedByA = (await accounts.GetAllAsync(Admin(SeedAdminId), Ct)).Single(u => u.Id == second);

        await accounts.UpdateAsync(Admin(SeedAdminId), second, loadedByA.Version, input with { Password = null, Role = UserRole.User }, Ct);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => accounts.UpdateAsync(Admin(SeedAdminId), second, loadedByA.Version, input with { Password = null, Email = "yeni@example.com" }, Ct));

        Assert.Equal(AccountService.ChangedElsewhere, error.Message);
        Assert.True(error.IsStale);
        var stored = (await accounts.GetAllAsync(Admin(SeedAdminId), Ct)).Single(u => u.Id == second);
        Assert.Equal((UserRole.User, "ikinci@example.com"), (stored.Role, stored.Email));

        await accounts.UpdateAsync(Admin(SeedAdminId), second, stored.Version, input with { Password = null, Role = UserRole.User, Email = "yeni@example.com" }, Ct);
        Assert.Equal("yeni@example.com", (await accounts.GetAllAsync(Admin(SeedAdminId), Ct)).Single(u => u.Id == second).Email);
    }

    /// <summary>
    /// A session keeps the role it signed in with, but the stored account decides: once another admin
    /// demotes it, its open session can no longer manage accounts, films or rentals, nor take the role back.
    /// </summary>
    [Fact]
    public async Task DemotedAdminSession_CannotManageAccountsOrFilms()
    {
        var accounts = _db.Get<IAccountService>();
        var films = _db.Get<IFilmService>();
        var rentals = _db.Get<IRentalService>();
        var auth = _db.Get<IAuthService>();
        var bInput = new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin);
        var b = await accounts.AddAsync(Admin(SeedAdminId), bInput, Ct);
        var sessionB = await auth.SignInAsync("ikinci", Password, Ct);
        Assert.True(sessionB.IsAdmin);
        var rental = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        var removable = await AddFilmAsync(_db.Services, "Silinmeyecek", 1);

        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), b, bInput with { Password = null, Role = UserRole.User }, Ct);

        List<Func<Task>> refused =
        [
            () => accounts.UpdateLoadedAsync(sessionB, b, bInput with { Password = null, Role = UserRole.Admin }, Ct),
            () => accounts.UpdateLoadedAsync(sessionB, SeedUserId, new AccountInput("ornek", "Ele.Gecir12", "ornek@example.com", "5550000002", UserRole.Admin), Ct),
            () => accounts.AddAsync(sessionB, new AccountInput("yedek", Password, "yedek@example.com", "5550000004", UserRole.Admin), Ct),
            () => accounts.DeleteAsync(sessionB, SeedUserId, Ct),
            () => accounts.GetAllAsync(sessionB, Ct),
            () => films.AddAsync(sessionB, new FilmInput("Yeni", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 1, true), Ct),
            () => films.UpdateLoadedAsync(sessionB, ThreeCopiesFilmId, new FilmInput("Degisti", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 9, true), Ct),
            () => films.DeleteAsync(sessionB, removable, Ct),
            () => films.GetAllAsync(sessionB, Ct),
            () => rentals.ReturnAsync(sessionB, rental.RentalId, Ct),
            () => rentals.GetActiveRentalsAsync(sessionB, FilmFilter.None, Ct),
        ];
        foreach (var operation in refused)
        {
            var error = await Assert.ThrowsAsync<AccessDeniedException>(operation);
            Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        }

        await using var db = _db.CreateContext();
        Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == b, Ct)).Role);
        Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == SeedUserId, Ct)).Role);
        Assert.Equal(PasswordFormat.Pbkdf2, (await db.Users.SingleAsync(u => u.Id == SeedUserId, Ct)).PasswordFormat);
        Assert.False(await db.Users.AnyAsync(u => u.UserName == "yedek", Ct));
        Assert.True(await db.Users.AnyAsync(u => u.Id == SeedUserId, Ct));
        Assert.False(await db.Films.AnyAsync(f => f.Title == "Yeni" || f.Title == "Degisti", Ct));
        Assert.True(await db.Films.AnyAsync(f => f.Id == removable, Ct));
        Assert.Equal(RentalStatus.Active, (await db.Rentals.SingleAsync(r => r.Id == rental.RentalId, Ct)).Status);
        await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);
    }

    /// <summary>A deleted account's open session can no longer rent or list rentals.</summary>
    [Fact]
    public async Task DeletedUserSession_CannotRentOrListRentals()
    {
        var gone = await RegisterAsync(_db.Services, "giden", 1);
        await _db.Get<IAccountService>().DeleteAsync(Admin(SeedAdminId), gone.UserId, Ct);
        var rentals = _db.Get<IRentalService>();

        var rent = await Assert.ThrowsAsync<AccessDeniedException>(async () => await rentals.RentAsync(gone, await rentals.QuoteAsync(ThreeCopiesFilmId, 25m, 1, Ct), Ct));
        var list = await Assert.ThrowsAsync<AccessDeniedException>(() => rentals.GetMyActiveRentalsAsync(gone, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, rent.Message);
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, list.Message);
        Assert.Equal(3, await AvailableAsync(ThreeCopiesFilmId));
    }

    /// <summary>A deleted account's open session can no longer search the catalog or read reviews.</summary>
    [Fact]
    public async Task DeletedUserSession_CannotSearchOrReadReviews()
    {
        var gone = await RegisterAsync(_db.Services, "okuyan", 1);
        await _db.Get<IAccountService>().DeleteAsync(Admin(SeedAdminId), gone.UserId, Ct);

        var search = await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IFilmService>().SearchCatalogAsync(gone, new CatalogQuery(), Ct));
        var reviews = await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IReviewService>().GetFilmReviewsAsync(gone, ThreeCopiesFilmId, Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, search.Message);
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, reviews.Message);
    }

    /// <summary>A deleted admin's open session can no longer manage anything.</summary>
    [Fact]
    public async Task DeletedAdminSession_CannotManageAccounts()
    {
        // Admins are never deleted by the application; another tool may still remove the row.
        var accounts = _db.Get<IAccountService>();
        var b = await accounts.AddAsync(Admin(SeedAdminId), new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin), Ct);
        var sessionB = await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct);
        await using (var db = _db.CreateContext())
        {
            await db.Users.Where(u => u.Id == b).ExecuteDeleteAsync(Ct);
        }

        var error = await Assert.ThrowsAsync<AccessDeniedException>(
            () => accounts.AddAsync(sessionB, new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin), Ct));

        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        await using var check = _db.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == b || u.UserName == "ikinci", Ct));
    }

    /// <summary>
    /// The stored session version decides too: once it differs from the one a session carries, that
    /// session reads and writes nothing, as an admin or as a customer, even though the account still exists
    /// with the same role.
    /// </summary>
    [Fact]
    public async Task SessionWithAnOutdatedVersion_IsRefusedEverywhere()
    {
        var auth = _db.Get<IAuthService>();
        var admin = await auth.SignInAsync("yonetici", "Demo.Yonetici1", Ct);
        var customer = await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);
        var rental = await RentAsync(_db.Services, customer, ThreeCopiesFilmId);
        await using (var db = _db.CreateContext())
        {
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1), Ct);
        }

        var films = _db.Get<IFilmService>();
        var rentals = _db.Get<IRentalService>();
        List<Func<Task>> refused =
        [
            () => _db.Get<IAccountService>().GetAllAsync(admin, Ct),
            () => films.AddAsync(admin, new FilmInput("Yeni", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 1, true), Ct),
            () => films.GetAllAsync(admin, Ct),
            () => rentals.ReturnAsync(admin, rental.RentalId, Ct),
            () => rentals.ReturnAsync(customer, rental.RentalId, Ct),
            () => rentals.GetMyActiveRentalsAsync(customer, Ct),
            () => films.SearchCatalogAsync(customer, new CatalogQuery(), Ct),
            async () => await rentals.RentAsync(customer, await rentals.QuoteAsync(SingleCopyFilmId, 25m, 1, Ct), Ct),
        ];
        foreach (var operation in refused)
        {
            Assert.Equal(AccessDeniedException.SessionRevokedMessage, (await Assert.ThrowsAsync<AccessDeniedException>(operation)).Message);
        }

        await using (var check = _db.CreateContext())
        {
            Assert.False(await check.Films.AnyAsync(f => f.Title == "Yeni", Ct));
        }

        Assert.Equal(2, await AvailableAsync(ThreeCopiesFilmId));

        // A new sign-in carries the stored version and works again.
        var renewed = await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);
        Assert.Equal(1, renewed.SessionVersion);
        await rentals.ReturnAsync(renewed, rental.RentalId, Ct);
    }

    /// <summary>
    /// A password set by another admin ends the account's open sessions even though its role stays the
    /// same: whoever still holds the old session can manage nothing more. A new sign-in with the new
    /// password works.
    /// </summary>
    [Fact]
    public async Task PasswordResetByAnAdmin_RevokesTheAccountsOpenSessions()
    {
        var accounts = _db.Get<IAccountService>();
        var auth = _db.Get<IAuthService>();
        var input = new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin);
        var second = await accounts.AddAsync(Admin(SeedAdminId), input, Ct);
        var stolen = await auth.SignInAsync("ikinci", Password, Ct);

        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), second, input with { Password = "Yeni.Sifre123" }, Ct);

        List<Func<Task>> refused =
        [
            () => accounts.GetAllAsync(stolen, Ct),
            () => accounts.UpdateLoadedAsync(stolen, second, input with { Password = "Geri.Al12345" }, Ct),
            () => _db.Get<IFilmService>().AddAsync(stolen, new FilmInput("Yeni", "Dram", "2020", "Ece Deneme", "10", null, "Deneme.", 1, true), Ct),
        ];
        foreach (var operation in refused)
        {
            Assert.Equal(AccessDeniedException.SessionRevokedMessage, (await Assert.ThrowsAsync<AccessDeniedException>(operation)).Message);
        }

        var fresh = await auth.SignInAsync("ikinci", "Yeni.Sifre123", Ct);
        Assert.Equal(1, fresh.SessionVersion);
        Assert.NotEmpty(await accounts.GetAllAsync(fresh, Ct));
    }

    /// <summary>
    /// A role taken away and given back still ends the sessions issued before: the version only rises.
    /// A change of the contact data alone ends nothing.
    /// </summary>
    [Fact]
    public async Task RoleTakenAndGivenBack_KeepsTheOldSessionRevoked()
    {
        var accounts = _db.Get<IAccountService>();
        var input = new AccountInput("ikinci", Password, "ikinci@example.com", "5550000003", UserRole.Admin);
        var second = await accounts.AddAsync(Admin(SeedAdminId), input, Ct);
        var old = await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct);

        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), second, input with { Password = null, Email = "yeni@example.com" }, Ct);
        Assert.NotEmpty(await accounts.GetAllAsync(old, Ct));

        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), second, input with { Password = null, Role = UserRole.User }, Ct);
        await accounts.UpdateLoadedAsync(Admin(SeedAdminId), second, input with { Password = null, Role = UserRole.Admin }, Ct);

        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => accounts.GetAllAsync(old, Ct));
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);
        Assert.Equal(2, (await _db.Get<IAuthService>().SignInAsync("ikinci", Password, Ct)).SessionVersion);
    }

    /// <summary>
    /// Changing one's own password keeps the session that changed it and ends every other session of the
    /// account, also one that signed in before with the same password.
    /// </summary>
    [Fact]
    public async Task OwnPasswordChange_KeepsThisSessionAndRevokesTheOthers()
    {
        var auth = _db.Get<IAuthService>();
        var here = await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);
        var elsewhere = await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct);

        await auth.ChangePasswordAsync(here, "Demo.Kullanici1", "Yeni.Sifre123", "Yeni.Sifre123", Ct);

        Assert.Equal(1, here.SessionVersion);
        await RentAsync(_db.Services, here, ThreeCopiesFilmId);
        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IRentalService>().GetMyActiveRentalsAsync(elsewhere, Ct));
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, error.Message);

        // The revoked session cannot change the password again either, even with the right one.
        var again = await Assert.ThrowsAsync<AccessDeniedException>(() => auth.ChangePasswordAsync(elsewhere, "Yeni.Sifre123", "Baska.Sifre123", "Baska.Sifre123", Ct));
        Assert.Equal(AccessDeniedException.SessionRevokedMessage, again.Message);
        await auth.SignInAsync("ornek", "Yeni.Sifre123", Ct);
    }

    /// <summary>A customer cannot make itself an admin, whatever its session says.</summary>
    [Fact]
    public async Task UserSession_CannotRaiseItsOwnRole()
    {
        var error = await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IAccountService>().UpdateLoadedAsync(
            User(SeedUserId),
            SeedUserId,
            new AccountInput("ornek", null, "ornek@example.com", "5550000002", UserRole.Admin),
            Ct));

        Assert.Equal(AccessDeniedException.DefaultMessage, error.Message);
        await using var db = _db.CreateContext();
        Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == SeedUserId, Ct)).Role);
    }

    [Fact]
    public async Task UpdateAccount_NewPasswordIsHashedAndLiftsTheLock()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", "Yanlis.Sifre1", Ct));
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", Password, Ct));
        await _db.Get<IAccountService>().UpdateLoadedAsync(
            Admin(SeedAdminId),
            other.UserId,
            new AccountInput("baska", "Yeni.Sifre12", "baska@example.com", "5540000001", UserRole.User),
            Ct);

        Assert.Equal(other.UserId, (await auth.SignInAsync("baska", "Yeni.Sifre12", Ct)).UserId);
    }

    [Fact]
    public async Task SignIn_FiveFailures_LockForFifteenMinutes()
    {
        await RegisterAsync(_db.Services, "baska", 1);
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", "Yanlis.Sifre1", Ct));
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", Password, Ct));
        _db.Clock.Advance(TimeSpan.FromMinutes(15));
        await auth.SignInAsync("baska", Password, Ct);
    }

    /// <summary>
    /// Attempts while the lock is in force, and attempts with a name no account has, make the same failure
    /// write as a wrong password, but it changes nothing: the counter, the lock's end and every other
    /// account stay as they were.
    /// </summary>
    [Fact]
    public async Task SignIn_WhileLockedOrUnknown_ChangesNoAccount()
    {
        await RegisterAsync(_db.Services, "baska", 1);
        var auth = _db.Get<IAuthService>();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", "Yanlis.Sifre1", Ct));
        }

        static async Task<List<(string, int, DateTime?)>> StateAsync(SqliteDatabase database)
        {
            await using var db = database.CreateContext();
            return await db.Users.OrderBy(u => u.Id).Select(u => ValueTuple.Create(u.UserName, u.FailedLoginCount, u.LockoutEnd)).ToListAsync(Ct);
        }

        var locked = await StateAsync(_db);
        _db.Clock.Advance(TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", "Yanlis.Sifre1", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", Password, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("kimse", Password, Ct));

        Assert.Equal(locked, await StateAsync(_db));
        Assert.Contains(locked, a => a.Item1 == "baska" && a.Item2 == 0 && a.Item3 != null);
    }

    [Fact]
    public async Task Register_DuplicateValues_AreReported()
    {
        await RegisterAsync(_db.Services, "baska", 1);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _db.Get<IAuthService>().RegisterAsync("baska2", Password, "baska@example.com", "5540000001", Ct));

        Assert.Contains("e-posta", error.Message, StringComparison.Ordinal);
        Assert.Contains("telefon", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePassword_TakesEffect()
    {
        var other = await RegisterAsync(_db.Services, "baska", 1);
        var auth = _db.Get<IAuthService>();

        await auth.ChangePasswordAsync(other, Password, "Yeni.Sifre12", "Yeni.Sifre12", Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("baska", Password, Ct));
        await auth.SignInAsync("baska", "Yeni.Sifre12", Ct);
    }

    [Fact]
    public async Task DemoAccounts_SignInWithTheDocumentedPasswords()
    {
        var auth = _db.Get<IAuthService>();

        Assert.True((await auth.SignInAsync("yonetici", "Demo.Yonetici1", Ct)).IsAdmin);
        Assert.False((await auth.SignInAsync("ornek", "Demo.Kullanici1", Ct)).IsAdmin);
    }

    private async Task<int> AvailableAsync(int filmId)
    {
        await using var db = _db.CreateContext();
        return await db.Films.Where(f => f.Id == filmId).Select(f => f.AvailableCopies).SingleAsync(Ct);
    }

    /// <summary>
    /// Admin A opened a film; admin B changed its price meanwhile. A's save of the old form (with the old
    /// price and another listing state) is refused as stale and writes nothing; after loading the film
    /// again A's change goes through. Rentals and returns move only the shelf, never the version.
    /// </summary>
    [Fact]
    public async Task UpdateFilm_FromAFormLoadedBeforeAnotherChange_IsRefused()
    {
        var films = _db.Get<IFilmService>();
        var loadedByA = await films.GetAsync(Admin(SeedAdminId), ThreeCopiesFilmId, Ct);
        var rental = await RentAsync(_db.Services, User(SeedUserId), ThreeCopiesFilmId);
        await _db.Get<IRentalService>().ReturnAsync(User(SeedUserId), rental.RentalId, Ct);
        Assert.Equal(loadedByA.Version, (await films.GetAsync(Admin(SeedAdminId), ThreeCopiesFilmId, Ct)).Version);

        await UpdateFilmAsync(ThreeCopiesFilmId, price: "40");
        var stale = new FilmInput(loadedByA.Title, loadedByA.Genre, loadedByA.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), loadedByA.Director, "25", loadedByA.ImageUrl, loadedByA.About, 9, false);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => films.UpdateAsync(Admin(SeedAdminId), ThreeCopiesFilmId, loadedByA.Version, stale, Ct));

        Assert.Equal(FilmService.ChangedElsewhere, error.Message);
        Assert.True(error.IsStale);
        var current = await films.GetAsync(Admin(SeedAdminId), ThreeCopiesFilmId, Ct);
        Assert.Equal((40m, 3, true, loadedByA.Version + 1), (current.DailyPrice, current.TotalCopies, current.IsListed, current.Version));

        await films.UpdateAsync(Admin(SeedAdminId), ThreeCopiesFilmId, current.Version, stale, Ct);
        var changed = await films.GetAsync(Admin(SeedAdminId), ThreeCopiesFilmId, Ct);
        Assert.Equal((25m, 9, false), (changed.DailyPrice, changed.TotalCopies, changed.IsListed));
    }

    private async Task UpdateFilmAsync(int filmId, string? price = null, int? copies = null, bool? listed = null)
    {
        var films = _db.Get<IFilmService>();
        var film = await films.GetAsync(Admin(SeedAdminId), filmId, Ct);
        await films.UpdateAsync(
            Admin(SeedAdminId),
            filmId,
            film.Version,
            new FilmInput(
                film.Title,
                film.Genre,
                film.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                film.Director,
                price ?? film.DailyPrice.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")),
                film.ImageUrl,
                film.About,
                copies ?? film.TotalCopies,
                listed ?? film.IsListed),
            Ct);
    }
}

/// <summary>A default installation: no demonstration data, no account, the first-run setup.</summary>
public sealed class FirstRunTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new(demo: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task EmptyDatabase_HasNoAccountAndAsksForTheSetup()
    {
        Assert.Equal(DemoSeedOutcome.NotRequested, _db.SeedOutcome);
        await using var db = _db.CreateContext();
        Assert.False(await db.Users.AnyAsync(Ct));
        Assert.True(await _db.Get<ISetupService>().IsSetupRequiredAsync(Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("admin", "admin", Ct));
    }

    [Fact]
    public async Task Setup_OnAnEmptyDatabase_CreatesTheFirstAdminOnce()
    {
        var setup = _db.Get<ISetupService>();

        var session = await setup.CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "ikinci", Password, Password, Ct));

        Assert.True(session.IsAdmin);
        Assert.Equal(SetupService.AlreadyDone, again.Message);
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", Password, Ct)).IsAdmin);
    }

    [Fact]
    public async Task Setup_WithAccountsButNoAdmin_NeedsAKeyThatWorksOnce()
    {
        await RegisterAsync(_db.Services, "musteri", 1);
        var setup = _db.Get<ISetupService>();

        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        Assert.True(await setup.IsAdminMissingAsync(Ct));
        Assert.Equal(SetupService.SetupKeyRequired, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct))).Message);
        Assert.Equal(SetupService.SetupKeyInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("0000-0000-0000-0000-0000-0000-0000-0000", "kurucu", Password, Password, Ct))).Message);

        var key = await AddSetupKeyAsync(_db.Services, Start);
        await setup.CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct);

        Assert.Equal(SetupService.AlreadyDone, (await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "ikinci", Password, Password, Ct))).Message);
        await using var db = _db.CreateContext();
        Assert.True(await db.SetupKeys.AllAsync(k => k.Consumed, Ct));
    }

    [Fact]
    public async Task Setup_ExpiredKey_IsRefused()
    {
        await RegisterAsync(_db.Services, "musteri", 1);
        var key = await AddSetupKeyAsync(_db.Services, Start);
        _db.Clock.Advance(TimeSpan.FromHours(25));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<ISetupService>().CreateFirstAdminAsync(key, "kurucu", Password, Password, Ct));

        Assert.Equal(SetupService.SetupKeyInvalid, error.Message);
    }

    [Fact]
    public async Task DemoMode_OnADatabaseWithData_AddsNothing()
    {
        await _db.Get<ISetupService>().CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct);

        await using var demo = _db.BuildServices(demo: true);
        var outcome = await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IDatabaseInitializer>(demo).InitializeAsync(Ct);

        Assert.Equal(DemoSeedOutcome.SkippedExistingData, outcome);
        await using var db = _db.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));
        Assert.False(await db.Films.AnyAsync(Ct));
    }
}
