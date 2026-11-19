using FilmKiralama.Business;
using FilmKiralama.Business.Services;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>Every write button runs through an exclusive operation: a second click while the first write waits is ignored.</summary>
public sealed class ExclusiveWriteTests
{
    [Fact]
    public void ExclusiveOperation_Failure_RestoresControlsAndAllowsTheNextRun() => Run(() =>
    {
        var operation = new ExclusiveOperation();
        using var enabled = new Button();
        using var disabled = new Button { Enabled = false };
        var pending = new TaskCompletionSource();

        var run = operation.RunAsync(() => pending.Task, enabled, disabled);
        Assert.True(operation.IsRunning);
        Assert.False(enabled.Enabled);
        Assert.False(operation.RunAsync(() => Task.CompletedTask).Result);

        pending.SetException(new InvalidOperationException("boom"));

        Assert.IsType<InvalidOperationException>(run.Exception?.InnerException);
        Assert.False(operation.IsRunning);
        Assert.True(enabled.Enabled);
        Assert.False(disabled.Enabled);
        Assert.True(operation.RunAsync(() => Task.CompletedTask).Result);
    });

    [Fact]
    public void SignIn_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        Find<Control>(form, "txtKullanici").Text = "ornek";
        Find<Control>(form, "txtSifre2").Text = "Gizli.Sifre1";

        var first = form.SignInAsync();
        var second = form.SignInAsync();

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Null(second.Result);
        Assert.Equal(1, auth.SignInCalls);
        Assert.False(Find<Control>(form, "btnGiris2").Enabled);
        Assert.False(Find<Control>(form, "btnOlustur2").Enabled);

        auth.CompleteSignIn(Sessions.User);
        Assert.Same(Sessions.User, first.Result);
        Assert.True(Find<Control>(form, "btnGiris2").Enabled);
    });

    [Fact]
    public void Register_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var auth = new PendingAuthService();
        var prompts = new RecordingPrompts();
        using var form = new RegisterForm(auth) { Prompts = prompts };

        var first = form.RegisterAsync();
        var second = form.RegisterAsync();

        Assert.False(second.Result);
        Assert.Equal(1, auth.RegisterCalls);
        auth.CompleteRegister();
        Assert.True(first.Result);
    });

    [Fact]
    public void AddFilm_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var films = new ControlledFilmService();
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };

        var first = form.AddAsync();
        var second = form.AddAsync();

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, films.AddCalls);
        Assert.False(Find<Button>(form, "btnFilmEkle").Enabled);
        Assert.False(Find<Button>(form, "btnFilmSil").Enabled);

        films.CompleteAdd();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(Find<Button>(form, "btnFilmEkle").Enabled);
    });

    [Fact]
    public void Rent_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var films = new ControlledFilmService();
        var rentals = new ControlledRentalService();
        var prompts = new RecordingPrompts();
        using var form = new KiralamaForm(films, rentals, new ControlledReviewService(), Sessions.User, 1) { Prompts = prompts };
        Assert.True(form.LoadFilmAsync().IsCompletedSuccessfully);
        Find<TextBox>(form, "txtGunSayisi").Text = "3";

        var first = form.RentAsync();
        var second = form.RentAsync();

        Assert.False(second.Result);
        Assert.Equal(1, rentals.RentCalls);
        Assert.Equal(3, rentals.LastQuote!.Days);
        Assert.Equal(12.5m, rentals.LastQuote.DailyPrice);
        Assert.Contains("Toplam Tutar: 37,50 TL", prompts.Messages[0], StringComparison.Ordinal);

        rentals.CompleteRent();
        Assert.True(first.Result);
    });

    [Fact]
    public void Rent_DaysBeyondTheSettings_AreCappedBeforeTheQuote() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new KiralamaForm(new ControlledFilmService(), rentals, new ControlledReviewService(), Sessions.User, 1) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadFilmAsync().IsCompletedSuccessfully);
        Find<TextBox>(form, "txtGunSayisi").Text = "999";

        _ = form.RentAsync();

        Assert.Equal(rentals.Policy.MaxRentalDays, rentals.LastQuote!.Days);
    });

    [Fact]
    public void Return_EveryReturnButtonStaysDisabledUntilTheReturnEnds() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new KullaniciProfil(rentals, new ControlledReviewService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        Assert.Equal(2, form.ReturnButtons.Count);

        var first = form.ReturnAsync(form.ShownRentals[0]);
        var second = form.ReturnAsync(form.ShownRentals[1]);

        Assert.False(second.Result);
        Assert.Equal(1, rentals.ReturnCalls);
        Assert.All(form.ReturnButtons, b => Assert.False(b.Enabled));

        rentals.Rentals.RemoveAt(0);
        rentals.CompleteReturn();
        Assert.True(first.Result);
        Assert.Single(form.ReturnButtons);
        Assert.True(form.ReturnButtons[0].Enabled);
    });

    [Fact]
    public void AddFilm_InvalidFields_AreMarkedNextToTheirControls() => Run(() =>
    {
        var films = new ControlledFilmService
        {
            AddFailure = new BusinessRuleException(
                "Lütfen gerekli alanları doldurun.",
                new Dictionary<string, string> { [nameof(FilmInput.Title)] = "Ad boş bırakılamaz.", [nameof(FilmInput.DailyPrice)] = "Ücret boş bırakılamaz." }),
        };
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var errors = (ErrorProvider)typeof(AdminPanel).GetField("errorProvider1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form)!;

        Assert.True(form.AddAsync().IsCompletedSuccessfully);

        Assert.Equal("Ad boş bırakılamaz.", errors.GetError(Find<TextBox>(form, "txtAd")));
        Assert.Equal("Ücret boş bırakılamaz.", errors.GetError(Find<TextBox>(form, "txtUcret")));
        Assert.Empty(errors.GetError(Find<TextBox>(form, "txtTur")));
        Assert.Contains(NoDialogs.Requested, r => r.Contains("Lütfen gerekli alanları doldurun.", StringComparison.Ordinal));
    });
}
