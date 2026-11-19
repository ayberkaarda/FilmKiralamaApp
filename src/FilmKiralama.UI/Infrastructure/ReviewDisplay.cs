using System.Globalization;
using System.Text;
using FilmKiralama.Business.Reviews;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Ratings and comments as the screens show them. Comments are user text: they are only ever placed in
/// text boxes or in labels without mnemonics, so "&amp;", markup or formula characters show as typed.
/// </summary>
internal static class ReviewDisplay
{
    /// <summary>Choices of the rating box, from the best.</summary>
    public static IReadOnlyList<RatingChoice> Choices { get; } =
    [
        new(5, "5 - Mükemmel"),
        new(4, "4 - Çok iyi"),
        new(3, "3 - İyi"),
        new(2, "2 - Vasat"),
        new(1, "1 - Kötü"),
    ];

    /// <summary>Filled and empty stars for <paramref name="rating"/> of five.</summary>
    public static string Stars(int rating)
    {
        var filled = Math.Clamp(rating, 0, ReviewPolicy.MaxRating);
        return new string('★', filled) + new string('☆', ReviewPolicy.MaxRating - filled);
    }

    /// <summary>"4,3 / 5 (12 değerlendirme)", or that there is no rating yet.</summary>
    public static string Summary(decimal? average, int count) =>
        count == 0 ? "Henüz değerlendirme yok" : $"{RatingMath.Format(average)} / 5 ({count} değerlendirme)";

    /// <summary>The reviews one below the other: author, stars and date, then the comment.</summary>
    public static string Describe(IReadOnlyList<PublicReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
        {
            return "Bu film için henüz yorum yapılmadı.";
        }

        var text = new StringBuilder();
        foreach (var review in reviews)
        {
            if (text.Length > 0)
            {
                text.Append(Environment.NewLine).Append(Environment.NewLine);
            }

            text.Append(review.Author).Append("  ").Append(Stars(review.Rating)).Append("  ").Append(DisplayFormat.Date(review.Date));
            if (review.Comment is { Length: > 0 } comment)
            {
                text.Append(Environment.NewLine).Append(comment);
            }
        }

        return text.ToString();
    }
}

/// <summary>A returned film of the profile list, with the rating the customer gave it.</summary>
internal sealed record ReviewableItem(ReviewableFilm Film)
{
    public override string ToString() =>
        $"{Film.Title} ({Film.Genre}, {Film.Year.ToString(CultureInfo.InvariantCulture)}) - "
        + (Film.MyRating is { } rating ? "Puanınız: " + ReviewDisplay.Stars(rating) : "henüz puan vermediniz");
}

/// <summary>A rating of the rating box.</summary>
internal sealed record RatingChoice(int Rating, string Text)
{
    public override string ToString() => Text;
}
