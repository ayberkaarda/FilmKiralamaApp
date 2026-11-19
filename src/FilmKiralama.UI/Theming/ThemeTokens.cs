namespace FilmKiralama.UI.Theming;

/// <summary>Spacing scale in logical pixels (96 DPI); layouts are scaled by Windows Forms for the monitor.</summary>
internal static class Spacing
{
    public const int XS = 4;
    public const int S = 8;
    public const int M = 12;
    public const int L = 16;
    public const int XL = 24;
    public const int XXL = 32;

    /// <summary>All steps in ascending order.</summary>
    public static IReadOnlyList<int> Scale { get; } = [XS, S, M, L, XL, XXL];
}

/// <summary>Corner radii in logical pixels; tighter than a rounded app tile, like the corners of a film frame.</summary>
internal static class Radius
{
    /// <summary>Buttons, inputs and status pills.</summary>
    public const int Control = 6;

    /// <summary>Cards, banners, badges and posters.</summary>
    public const int Card = 10;
}

/// <summary>Fixed control sizes in logical pixels.</summary>
internal static class Sizes
{
    /// <summary>Height of buttons, text fields and drop-down lists, so a row of them lines up.</summary>
    public const int ControlHeight = 34;

    public const int ButtonMinWidth = 96;

    /// <summary>Glyph badge in screen headers and message windows.</summary>
    public const int HeaderBadge = 40;

    /// <summary>Logo badge of the brand panel.</summary>
    public const int LogoBadge = 64;

    /// <summary>Width of the brand panel on the sign-in screen.</summary>
    public const int BrandPanelWidth = 312;

    /// <summary>Height of a multi-line text field.</summary>
    public const int NoteHeight = 76;

    /// <summary>Width of a film card in the catalog; its poster keeps the 2:3 shape of a film poster.</summary>
    public const int PosterCardWidth = 196;

    /// <summary>Width of the small poster beside a rental.</summary>
    public const int PosterThumbWidth = 72;

    /// <summary>Width of the poster on the rental screen.</summary>
    public const int PosterLargeWidth = 216;

    public const int FocusRing = 2;
}

/// <summary>One step of the type ramp; sizes are in points. <paramref name="Fallback"/> is used where <paramref name="Family"/> is not installed.</summary>
internal readonly record struct FontToken(string Family, float Size, FontStyle Style = FontStyle.Regular, string? Fallback = null);

/// <summary>
/// Type ramp. Headings use Franklin Gothic Medium, the condensed-feeling grotesque of film credits and cinema
/// marquees, which ships with Windows; where it is missing they fall back to Segoe UI Semibold. Body text is
/// Segoe UI at 9.75 pt, so film lists and forms stay quick to scan. Hierarchy comes from size and weight, never
/// from color alone. Display &gt; Title &gt; Subtitle &gt; Body &gt; Caption.
/// </summary>
internal static class Typography
{
    public const string Family = "Segoe UI";
    public const string SemiboldFamily = "Segoe UI Semibold";
    public const string HeadingFamily = "Franklin Gothic Medium";

    public static readonly FontToken Display = new(HeadingFamily, 22F, Fallback: SemiboldFamily);
    public static readonly FontToken Title = new(HeadingFamily, 15.75F, Fallback: SemiboldFamily);
    public static readonly FontToken Subtitle = new(HeadingFamily, 12F, Fallback: SemiboldFamily);
    public static readonly FontToken Body = new(Family, 9.75F);
    public static readonly FontToken BodyStrong = new(SemiboldFamily, 9.75F);
    public static readonly FontToken Caption = new(Family, 9F);

    /// <summary>An amount the user decides on, such as the total of a rental.</summary>
    public static readonly FontToken Figure = new(HeadingFamily, 18F, Fallback: SemiboldFamily);

    /// <summary>Text sizes from the smallest to the largest; used to check the ramp is ordered.</summary>
    public static IReadOnlyList<FontToken> Ramp { get; } = [Caption, Body, Subtitle, Title, Display];
}

/// <summary>Code points of Segoe Fluent Icons / Segoe MDL2 Assets used by the application.</summary>
internal static class Glyphs
{
    public const string Film = "";
    public const string Video = "";
    public const string Play = "";
    public const string Person = "";
    public const string People = "";
    public const string AddPerson = "";
    public const string Lock = "";
    public const string Key = "";
    public const string Admin = "";
    public const string Add = "";
    public const string Edit = "";
    public const string Save = "";
    public const string Delete = "";
    public const string Cancel = "";
    public const string List = "";
    public const string Watchlist = "";
    public const string Star = "";
    public const string Comment = "";
    public const string Hide = "";
    public const string View = "";
    public const string Chart = "";
    public const string Download = "";
    public const string Calendar = "";
    public const string Return = "";
    public const string Cart = "";
    public const string Picture = "";
    public const string Search = "";
    public const string Filter = "";
    public const string Refresh = "";
    public const string SignOut = "";
    public const string Like = "";
    public const string Info = "";
    public const string Success = "";
    public const string Warning = "";
    public const string Error = "";
    public const string Question = "";
    public const string CheckMark = "";
    public const string ChevronDown = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
}
