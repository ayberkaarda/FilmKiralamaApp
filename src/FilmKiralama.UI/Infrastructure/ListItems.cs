using System.ComponentModel;
using System.Globalization;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>A filter choice of a combo box; "Tümü" has no value.</summary>
internal sealed record FilterChoice(string Text, string? Value)
{
    public const string All = "Tümü";

    public static FilterChoice Any { get; } = new(All, null);

    public override string ToString() => Text;
}

/// <summary>
/// The genre, director and year combo boxes of the film lists. Filling them is not a user choice: while
/// the options are replaced no change is reported, and the previous choice is kept when it still exists.
/// </summary>
internal sealed class FilmFilterBoxes
{
    private readonly ComboBox _genre;
    private readonly ComboBox _director;
    private readonly ComboBox _year;
    private bool _filling;

    public FilmFilterBoxes(ComboBox genre, ComboBox director, ComboBox year)
    {
        _genre = genre;
        _director = director;
        _year = year;
        foreach (var box in new[] { genre, director, year })
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Add(FilterChoice.Any);
            box.SelectedIndex = 0;
            box.SelectedIndexChanged += (_, _) =>
            {
                if (!_filling)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            };
        }
    }

    /// <summary>Raised when the user picks another value.</summary>
    public event EventHandler? Changed;

    public FilmFilter Current => new(
        Selected(_genre),
        Selected(_director),
        Selected(_year) is { } year ? int.Parse(year, CultureInfo.InvariantCulture) : null);

    public void Fill(FilmFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _filling = true;
        try
        {
            Fill(_genre, options.Genres);
            Fill(_director, options.Directors);
            Fill(_year, options.Years.Select(y => y.ToString(CultureInfo.InvariantCulture)));
        }
        finally
        {
            _filling = false;
        }
    }

    private static void Fill(ComboBox box, IEnumerable<string> values)
    {
        var previous = Selected(box);
        box.ReplaceItems(new[] { FilterChoice.Any }.Concat(values.Select(v => new FilterChoice(v, v))));
        var index = 0;
        for (var i = 1; i < box.Items.Count; i++)
        {
            if (box.Items[i] is FilterChoice choice && string.Equals(choice.Value, previous, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        box.SelectedIndex = index;
    }

    private static string? Selected(ComboBox box) => (box.SelectedItem as FilterChoice)?.Value;
}

/// <summary>A film row of the admin's film grid; the column headers are the original column names.</summary>
internal sealed class FilmRow(Film film)
{
    [Browsable(false)]
    public Film Film { get; } = film;

    [DisplayName("FilmID")]
    public int Id => Film.Id;

    [DisplayName("Ad")]
    public string Title => Film.Title;

    [DisplayName("Tur")]
    public string Genre => Film.Genre;

    [DisplayName("Yil")]
    public int Year => Film.Year;

    [DisplayName("Yonetmen")]
    public string Director => Film.Director;

    [DisplayName("GunlukUcret")]
    public string DailyPrice => DisplayFormat.MoneyInput(Film.DailyPrice);

    [DisplayName("Adet")]
    public int TotalCopies => Film.TotalCopies;

    [DisplayName("MevcutAdet")]
    public int AvailableCopies => Film.AvailableCopies;

    [DisplayName("Durum")]
    public string Status => Describe(Film);

    [DisplayName("Image")]
    public string ImageUrl => Film.ImageUrl ?? string.Empty;

    [DisplayName("Hakkinda")]
    public string About => Film.About ?? string.Empty;

    /// <summary>The state the original status column showed, now derived from the stock and the listing.</summary>
    public static string Describe(Film film)
    {
        ArgumentNullException.ThrowIfNull(film);
        if (!film.IsListed)
        {
            return "Kiralamaya kapalı";
        }

        return film.AvailableCopies > 0 ? "Müsait" : "Kirada";
    }
}

/// <summary>An account row of the admin's account grid; the password is never shown.</summary>
internal sealed class UserRow(UserSummary user)
{
    [Browsable(false)]
    public UserSummary User { get; } = user;

    [DisplayName("KullaniciID")]
    public int Id => User.Id;

    [DisplayName("KullaniciAdi")]
    public string UserName => User.UserName;

    [DisplayName("Eposta")]
    public string Email => User.Email ?? string.Empty;

    [DisplayName("Telefon")]
    public string Phone => User.Phone ?? string.Empty;

    [DisplayName("Rol")]
    public string Role => RoleChoice.Describe(User.Role);
}

/// <summary>A role of the account form, shown with the original names ("Admin", "Müşteri").</summary>
internal sealed record RoleChoice(UserRole Role)
{
    public static IReadOnlyList<RoleChoice> All { get; } = [new(UserRole.Admin), new(UserRole.User)];

    public static string Describe(UserRole role) => role == UserRole.Admin ? "Admin" : "Müşteri";

    public override string ToString() => Describe(Role);
}
