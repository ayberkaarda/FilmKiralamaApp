using System.Globalization;
using FilmKiralama.UI;
using FilmKiralama.UI.Infrastructure;

namespace FilmKiralama.Screenshots;

/// <summary>The screens shown in the README, each prepared with the invented shop as a customer or an admin would see it.</summary>
internal static class Screens
{
    /// <summary>File name (kebab-case, without extension) and how to build the screen.</summary>
    public static IReadOnlyList<(string Name, Func<SyntheticCinema, Form> Build)> All { get; } =
    [
        ("giris", cinema => new LoginForm(cinema, new UnusedFormFactory())),
        ("kayit-ol", SignUp),
        ("ilk-kurulum", cinema => new IlkKurulumForm(cinema) { RequireSetupKey = true }),
        ("katalog", Catalog),
        ("kiralama", Rental),
        ("profil", Profile),
        ("degerlendirme", Review),
        ("izleme-listesi", Watchlist),
        ("oneriler", Recommendations),
        ("yonetim-paneli", AdminPanel),
        ("kiralanan-filmler", Rentals),
        ("hesaplar", Accounts),
        ("yorumlar", Reviews),
        ("raporlar", Reports),
        ("onay-penceresi", Confirmation),
    ];

    /// <summary>A sign-up being typed; the password stays empty.</summary>
    private static RegisterForm SignUp(SyntheticCinema cinema)
    {
        var form = new RegisterForm(cinema);
        Find<TextBox>(form, "txtUsername").Text = "deniz.su";
        Find<TextBox>(form, "txtMail").Text = "deniz.su@example.com";
        Find<TextBox>(form, "txtTelefon").Text = "5553601122";
        return form;
    }

    /// <summary>The question asked before a film is deleted.</summary>
    private static MessageDialog Confirmation(SyntheticCinema cinema) => new(
        "Seçili filmi silmek istediğinize emin misiniz? Kiralama geçmişi olan bir film silinemez; onun yerine kiralamaya kapatabilirsiniz.",
        "Onay",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question);

    /// <summary>The review moderation with a hidden review chosen.</summary>
    private static YorumYonetimForm Reviews(SyntheticCinema cinema)
    {
        var form = new YorumYonetimForm(cinema, cinema.Admin);
        Run(form.LoadAsync(1));
        Select(Find<DataGridView>(form, "dgvYorumlar"), 2);
        return form;
    }

    /// <summary>The most rented films of the last month.</summary>
    private static RaporlarForm Reports(SyntheticCinema cinema)
    {
        var form = new RaporlarForm(cinema, cinema.Admin, new FixedClock(new DateTimeOffset(2027, 3, 18, 10, 0, 0, TimeSpan.Zero)));
        Run(form.LoadAsync());
        return form;
    }

    /// <summary>A clock that always shows the same moment, so periods do not depend on the day the tool runs.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>The admin panel with a film chosen in the table and its details in the editor.</summary>
    private static AdminPanel AdminPanel(SyntheticCinema cinema)
    {
        var form = new AdminPanel(cinema, new UnusedFormFactory(), cinema.Admin) { Posters = cinema.Posters };
        Run(form.RefreshAsync());
        var grid = Find<DataGridView>(form, "dataGridView1");
        Select(grid, 3);
        form.SelectFilm(((FilmRow)grid.Rows[3].DataBoundItem!).Film);
        return form;
    }

    /// <summary>Every active rental with the account that rented it.</summary>
    private static KiralananFilmlerPanel Rentals(SyntheticCinema cinema)
    {
        var form = new KiralananFilmlerPanel(cinema, cinema, cinema.Admin) { Posters = cinema.Posters };
        Run(form.RefreshAsync());
        return form;
    }

    /// <summary>The accounts with one chosen for a change.</summary>
    private static KullaniciYonetimPanel Accounts(SyntheticCinema cinema)
    {
        var form = new KullaniciYonetimPanel(cinema, cinema.Admin);
        Run(form.LoadUsersAsync());
        Select(Find<DataGridView>(form, "dataGridView1"), 3);
        form.SelectUser(form.ShownUsers[3]);
        return form;
    }

    /// <summary>The watchlist with a film that can be rented now selected.</summary>
    private static IzlemeListesiForm Watchlist(SyntheticCinema cinema)
    {
        var form = new IzlemeListesiForm(cinema, new UnusedFormFactory(), cinema.User);
        Run(form.LoadAsync());
        Select(Find<DataGridView>(form, "dgvListe"), 0);
        return form;
    }

    /// <summary>The recommendations from the genre the customer rents most.</summary>
    private static OnerilerForm Recommendations(SyntheticCinema cinema)
    {
        var form = new OnerilerForm(cinema, cinema, new UnusedFormFactory(), cinema.User);
        Run(form.LoadAsync());
        Select(Find<DataGridView>(form, "dgvOneriler"), 1);
        return form;
    }

    /// <summary>Selects a row as a click on it does.</summary>
    private static void Select(DataGridView grid, int row)
    {
        _ = grid.Handle;
        grid.CurrentCell = grid.Rows[row].Cells[0];
        grid.Rows[row].Selected = true;
    }

    /// <summary>The customer's profile: three films out and the films to rate, one of them chosen.</summary>
    private static KullaniciProfil Profile(SyntheticCinema cinema)
    {
        var form = new KullaniciProfil(cinema, cinema, new UnusedFormFactory(), cinema.User) { Posters = cinema.Posters };
        Run(form.LoadRentalsAsync());
        Run(form.LoadReviewableAsync());
        Find<ListBox>(form, "lstPuanlanabilir").SelectedIndex = 1;
        return form;
    }

    /// <summary>The customer's own review of a returned film, ready to be changed.</summary>
    private static DegerlendirmeForm Review(SyntheticCinema cinema)
    {
        var film = cinema.GetReviewableAsync(cinema.User).Result[0];
        var form = new DegerlendirmeForm(cinema, cinema.User, film);
        Run(form.LoadAsync());
        return form;
    }

    /// <summary>The customer's catalog with the first page of films, posters drawn in code.</summary>
    private static AnaForm Catalog(SyntheticCinema cinema)
    {
        var form = new AnaForm(cinema, cinema, new UnusedFormFactory(), cinema.User) { Posters = cinema.Posters };
        Run(form.RefreshAsync());
        return form;
    }

    /// <summary>The rental of a film with its reviews, four days chosen.</summary>
    private static KiralamaForm Rental(SyntheticCinema cinema)
    {
        var form = new KiralamaForm(cinema, cinema, cinema, cinema.User, 2) { Posters = cinema.Posters };
        Run(form.LoadFilmAsync());
        form.ChooseDays(4);
        return form;
    }

    /// <summary>The in-memory services complete synchronously; anything else is a bug in the tool.</summary>
    internal static void Run(Task task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("A screen did not finish loading synchronously.");
        }

        task.GetAwaiter().GetResult();
    }

    internal static T Find<T>(Control root, string name)
        where T : Control =>
        root.Controls.Find(name, searchAllChildren: true).OfType<T>().SingleOrDefault()
        ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Control '{0}' was not found.", name));

    /// <summary>No other window is opened while a screen is drawn.</summary>
    private sealed class UnusedFormFactory : IFormFactory
    {
        public T Create<T>(params object[] arguments)
            where T : Form => throw new InvalidOperationException("Screens are not opened while capturing.");
    }
}
