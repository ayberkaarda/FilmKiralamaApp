namespace FilmKiralama.UI.Theming;

/// <summary>
/// Named colors of one theme. Controls never use literal colors; they read a role from the current
/// palette, so the light, dark and high contrast themes stay consistent.
/// </summary>
internal sealed record ThemePalette
{
    public required string Name { get; init; }

    public bool IsDark { get; init; }

    /// <summary>Built from the Windows high contrast colors; custom painting steps back for native drawing.</summary>
    public bool IsHighContrast { get; init; }

    /// <summary>Window background behind cards: a soft grey with a faint plum tint, like a foyer wall.</summary>
    public required Color Page { get; init; }

    /// <summary>Cards, inputs, header and footer.</summary>
    public required Color Surface { get; init; }

    /// <summary>Quiet areas: disabled inputs, table headers and the footer of the message dialog.</summary>
    public required Color SurfaceAlt { get; init; }

    public required Color SurfaceHover { get; init; }

    /// <summary>Decorative hairlines between areas.</summary>
    public required Color Border { get; init; }

    /// <summary>Borders of inputs; at least 3:1 against the surface.</summary>
    public required Color BorderStrong { get; init; }

    public required Color Text { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color TextMuted { get; init; }

    public required Color TextDisabled { get; init; }

    /// <summary>The velvet plum of the logo: primary actions, selection and the brand panel.</summary>
    public required Color Accent { get; init; }

    public required Color AccentHover { get; init; }

    public required Color AccentPressed { get; init; }

    /// <summary>Text and glyphs on <see cref="Accent"/>.</summary>
    public required Color OnAccent { get; init; }

    /// <summary>Secondary text on <see cref="Accent"/>, for example the tagline of the brand panel.</summary>
    public required Color OnAccentMuted { get; init; }

    /// <summary>Tinted background for selected rows and glyph badges.</summary>
    public required Color AccentSubtle { get; init; }

    /// <summary>Accent used as text or a glyph on <see cref="Surface"/>.</summary>
    public required Color AccentText { get; init; }

    /// <summary>
    /// The marquee gold of the logo's light: rating stars, a film on the watchlist and the brand line.
    /// Decorative only; text in this family uses <see cref="WarmText"/>.
    /// </summary>
    public required Color Warm { get; init; }

    public required Color WarmSubtle { get; init; }

    public required Color WarmText { get; init; }

    public required Color Success { get; init; }

    public required Color SuccessSubtle { get; init; }

    public required Color Warning { get; init; }

    public required Color WarningSubtle { get; init; }

    public required Color Danger { get; init; }

    public required Color DangerSubtle { get; init; }

    public required Color Info { get; init; }

    public required Color InfoSubtle { get; init; }

    /// <summary>
    /// Light theme: plum-tinted neutrals with a velvet plum accent and a marquee gold mark. The plum stays far
    /// from the red danger role, so deleting a film or returning a rental never looks like the primary action,
    /// and the gold stays apart from the burnt orange of warnings.
    /// </summary>
    public static ThemePalette Light { get; } = new()
    {
        Name = "Light",
        Page = Hex(0xF4F2F6),
        Surface = Hex(0xFFFFFF),
        SurfaceAlt = Hex(0xF8F6FA),
        SurfaceHover = Hex(0xF2EDF7),
        Border = Hex(0xE3DDE9),
        BorderStrong = Hex(0x7D7489),
        Text = Hex(0x1C1524),
        TextSecondary = Hex(0x463D51),
        TextMuted = Hex(0x5F566B),
        TextDisabled = Hex(0xACA5B5),
        Accent = Hex(0x7A2E9E),
        AccentHover = Hex(0x6A2689),
        AccentPressed = Hex(0x5A2075),
        OnAccent = Hex(0xFFFFFF),
        OnAccentMuted = Hex(0xEBDDF3),
        AccentSubtle = Hex(0xF4EBF9),
        AccentText = Hex(0x6E2891),
        Warm = Hex(0xC98516),
        WarmSubtle = Hex(0xFCF1DD),
        WarmText = Hex(0x865100),
        Success = Hex(0x1B6E45),
        SuccessSubtle = Hex(0xE5F4EC),
        Warning = Hex(0x9A3D0A),
        WarningSubtle = Hex(0xFDEDE1),
        Danger = Hex(0xB42318),
        DangerSubtle = Hex(0xFCEAE8),
        Info = Hex(0x2F4E8A),
        InfoSubtle = Hex(0xE9EEF8),
    };

    /// <summary>Dark theme: a darkened auditorium, the same roles stepped for a near-black plum surface, not an inverted light theme.</summary>
    public static ThemePalette Dark { get; } = new()
    {
        Name = "Dark",
        IsDark = true,
        Page = Hex(0x120E17),
        Surface = Hex(0x1B1622),
        SurfaceAlt = Hex(0x211B29),
        SurfaceHover = Hex(0x2A2234),
        Border = Hex(0x352C40),
        BorderStrong = Hex(0x7F7590),
        Text = Hex(0xEEE8F3),
        TextSecondary = Hex(0xC6BCD0),
        TextMuted = Hex(0xA398AF),
        TextDisabled = Hex(0x5E546B),
        Accent = Hex(0xCF97EC),
        AccentHover = Hex(0xD8A9F2),
        AccentPressed = Hex(0xC188E6),
        OnAccent = Hex(0x22102E),
        OnAccentMuted = Hex(0x3E2550),
        AccentSubtle = Hex(0x34224A),
        AccentText = Hex(0xD6A6F5),
        Warm = Hex(0xF2B54A),
        WarmSubtle = Hex(0x3A2C12),
        WarmText = Hex(0xF5C877),
        Success = Hex(0x7FD3A0),
        SuccessSubtle = Hex(0x14302A),
        Warning = Hex(0xF4A36B),
        WarningSubtle = Hex(0x3A2416),
        Danger = Hex(0xFF9C8C),
        DangerSubtle = Hex(0x3C1B16),
        Info = Hex(0xA3B5F2),
        InfoSubtle = Hex(0x1C2340),
    };

    /// <summary>
    /// High contrast theme built from the active Windows system colors, so the user's contrast theme is
    /// respected instead of being replaced by the application's colors. Selected and hovered content is
    /// written in HighlightText on Highlight.
    /// </summary>
    public static ThemePalette HighContrast() => new()
    {
        Name = "HighContrast",
        IsHighContrast = true,
        IsDark = SystemColors.Window.GetBrightness() < 0.5f,
        Page = SystemColors.Window,
        Surface = SystemColors.Window,
        SurfaceAlt = SystemColors.Window,
        SurfaceHover = SystemColors.Highlight,
        Border = SystemColors.WindowText,
        BorderStrong = SystemColors.WindowText,
        Text = SystemColors.WindowText,
        TextSecondary = SystemColors.WindowText,
        TextMuted = SystemColors.WindowText,
        TextDisabled = SystemColors.GrayText,
        Accent = SystemColors.Highlight,
        AccentHover = SystemColors.Highlight,
        AccentPressed = SystemColors.Highlight,
        OnAccent = SystemColors.HighlightText,
        OnAccentMuted = SystemColors.HighlightText,
        AccentSubtle = SystemColors.Window,
        AccentText = SystemColors.HotTrack,
        Warm = SystemColors.WindowText,
        WarmSubtle = SystemColors.Window,
        WarmText = SystemColors.WindowText,
        Success = SystemColors.WindowText,
        SuccessSubtle = SystemColors.Window,
        Warning = SystemColors.WindowText,
        WarningSubtle = SystemColors.Window,
        Danger = SystemColors.WindowText,
        DangerSubtle = SystemColors.Window,
        Info = SystemColors.WindowText,
        InfoSubtle = SystemColors.Window,
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
