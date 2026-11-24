using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace FilmKiralama.Screenshots;

/// <summary>
/// Poster art for the invented films of the screenshots, drawn in code: a gradient, one geometric motif (a sun
/// over hills, rings, bands, a night skyline, waves or a halftone disc) and the title set in the heading face.
/// Nothing is downloaded and no real poster, title, studio or person appears; the same film always gets the
/// same poster.
/// </summary>
internal static class PosterArt
{
    public const int Width = 480;
    public const int Height = 720;

    private static readonly (Color Deep, Color Light, Color Accent)[] Palettes =
    [
        (Hex(0x1D2B53), Hex(0x7E97C7), Hex(0xFF7A59)),
        (Hex(0x2B1B3D), Hex(0x8C5FB3), Hex(0xF2B54A)),
        (Hex(0x0F3D3E), Hex(0x5E9C8F), Hex(0xE8D5B7)),
        (Hex(0x3A0F2E), Hex(0xA34A6B), Hex(0xFF9F1C)),
        (Hex(0x102A43), Hex(0x486581), Hex(0x9FD8F5)),
        (Hex(0x1B4332), Hex(0x52B788), Hex(0xFFE8A3)),
        (Hex(0x2E1F6B), Hex(0x6B5BD6), Hex(0xF15BB5)),
        (Hex(0x4A1C1C), Hex(0xB5523B), Hex(0xF4D35E)),
    ];

    /// <summary>The poster of a film: <paramref name="seed"/> picks the colors and the motif.</summary>
    public static Bitmap Draw(int seed, string title, int year)
    {
        var random = new Random(seed * 7919);
        var (deep, light, accent) = Palettes[seed % Palettes.Length];
        // Opaque: GDI text on a bitmap with an alpha channel would come out transparent.
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), light, deep, LinearGradientMode.Vertical))
        {
            g.FillRectangle(sky, 0, 0, Width, Height);
        }

        switch (seed % 6)
        {
            case 0:
                SunOverHills(g, random, deep, accent);
                break;
            case 1:
                Rings(g, random, deep, light, accent);
                break;
            case 2:
                Bands(g, random, deep, light, accent);
                break;
            case 3:
                Skyline(g, random, deep, accent);
                break;
            case 4:
                Waves(g, random, deep, light, accent);
                break;
            default:
                Halftone(g, random, deep, accent);
                break;
        }

        Caption(g, deep, accent, title, year);
        return bitmap;
    }

    /// <summary>
    /// A poster from a textless picture (generated once, kept under <c>tools/Screenshots/posters</c>): the picture
    /// is cropped to the poster's ratio and the title and year are set over it in code, so the lettering is
    /// always correct.
    /// </summary>
    public static Bitmap Compose(Image art, string title, int year)
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var scale = Math.Max((float)Width / art.Width, (float)Height / art.Height);
        var cropWidth = Width / scale;
        var cropHeight = Height / scale;
        g.DrawImage(art, new Rectangle(0, 0, Width, Height), (art.Width - cropWidth) / 2, (art.Height - cropHeight) / 2, cropWidth, cropHeight, GraphicsUnit.Pixel);

        Caption(g, Color.FromArgb(16, 16, 24), Hex(0xF2B54A), title, year);
        return bitmap;
    }

    private static void SunOverHills(Graphics g, Random random, Color deep, Color accent)
    {
        var size = 220 + random.Next(80);
        using (var sun = new SolidBrush(accent))
        {
            g.FillEllipse(sun, (Width - size) / 2 + random.Next(-60, 60), 120 + random.Next(60), size, size);
        }

        for (var layer = 0; layer < 3; layer++)
        {
            var baseline = 330 + (layer * 70);
            using var path = new GraphicsPath();
            var points = new List<PointF> { new(0, Height) };
            for (var x = 0; x <= Width; x += 60)
            {
                points.Add(new PointF(x, baseline - random.Next(30, 110)));
            }

            points.Add(new PointF(Width, Height));
            path.AddLines([.. points]);
            path.CloseFigure();
            using var hill = new SolidBrush(Blend(deep, Color.Black, 0.15 + (layer * 0.18)));
            g.FillPath(hill, path);
        }
    }

    private static void Rings(Graphics g, Random random, Color deep, Color light, Color accent)
    {
        var center = new PointF(Width / 2F + random.Next(-50, 50), 300 + random.Next(-40, 40));
        for (var i = 9; i >= 1; i--)
        {
            var radius = i * 34F;
            var color = i % 2 == 0 ? Blend(deep, light, 0.35) : i == 1 ? accent : Blend(deep, Color.Black, 0.2);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
        }

        using var pen = new Pen(accent, 6);
        g.DrawLine(pen, center.X - 300, center.Y + 300, center.X + 300, center.Y - 300);
    }

    private static void Bands(Graphics g, Random random, Color deep, Color light, Color accent)
    {
        var colors = new[] { accent, Blend(light, Color.White, 0.25), Blend(deep, Color.Black, 0.25), light };
        var width = 70 + random.Next(30);
        g.RotateTransform(-28);
        for (var i = -6; i < 12; i++)
        {
            using var band = new SolidBrush(Color.FromArgb(i % 3 == 0 ? 230 : 120, colors[Math.Abs(i) % colors.Length]));
            g.FillRectangle(band, (i * width * 1.6F) - 200, -200, width, Height * 2);
        }

        g.ResetTransform();
        using var shade = new LinearGradientBrush(new Rectangle(0, Height / 2, Width, Height / 2), Color.FromArgb(0, deep), deep, LinearGradientMode.Vertical);
        g.FillRectangle(shade, 0, Height / 2, Width, Height / 2);
    }

    private static void Skyline(Graphics g, Random random, Color deep, Color accent)
    {
        using (var moon = new SolidBrush(Blend(accent, Color.White, 0.4)))
        {
            g.FillEllipse(moon, 300 + random.Next(60), 70 + random.Next(50), 90, 90);
        }

        var x = 0;
        while (x < Width)
        {
            var w = 40 + random.Next(50);
            var h = 160 + random.Next(260);
            var top = 560 - h;
            using (var building = new SolidBrush(Blend(deep, Color.Black, 0.3 + (random.NextDouble() * 0.3))))
            {
                g.FillRectangle(building, x, top, w, Height - top);
            }

            using var window = new SolidBrush(Color.FromArgb(200, accent));
            for (var wy = top + 14; wy < 540; wy += 22)
            {
                for (var wx = x + 8; wx < x + w - 10; wx += 16)
                {
                    if (random.Next(3) == 0)
                    {
                        g.FillRectangle(window, wx, wy, 7, 10);
                    }
                }
            }

            x += w + 4;
        }
    }

    private static void Waves(Graphics g, Random random, Color deep, Color light, Color accent)
    {
        using (var moon = new SolidBrush(accent))
        {
            g.FillEllipse(moon, 70 + random.Next(200), 110, 130, 130);
        }

        for (var band = 0; band < 7; band++)
        {
            var top = 330 + (band * 52);
            var amplitude = 14 + random.Next(16);
            var phase = random.NextDouble() * Math.PI;
            using var path = new GraphicsPath();
            var points = new List<PointF> { new(0, Height) };
            for (var px = 0; px <= Width; px += 12)
            {
                points.Add(new PointF(px, top + (float)(Math.Sin((px / 60.0) + phase) * amplitude)));
            }

            points.Add(new PointF(Width, Height));
            path.AddLines([.. points]);
            path.CloseFigure();
            using var water = new SolidBrush(Blend(light, deep, 0.25 + (band * 0.11)));
            g.FillPath(water, path);
        }
    }

    private static void Halftone(Graphics g, Random random, Color deep, Color accent)
    {
        var center = new PointF(Width / 2F, 290 + random.Next(-30, 30));
        using var dot = new SolidBrush(accent);
        for (var y = 20; y < 600; y += 24)
        {
            for (var x = 12; x < Width; x += 24)
            {
                var distance = Math.Sqrt(Math.Pow(x - center.X, 2) + Math.Pow(y - center.Y, 2));
                var radius = (float)Math.Max(0, 11 - (distance / 22));
                if (radius > 0.6)
                {
                    g.FillEllipse(dot, x - radius, y - radius, 2 * radius, 2 * radius);
                }
            }
        }

        using var bar = new SolidBrush(Blend(deep, Color.Black, 0.2));
        g.FillRectangle(bar, 0, 470, Width, 12);
    }

    /// <summary>The title in capitals over a dark foot, the year under a short rule.</summary>
    private static void Caption(Graphics g, Color deep, Color accent, string title, int year)
    {
        using (var foot = new LinearGradientBrush(new Rectangle(0, 470, Width, Height - 470), Color.FromArgb(0, deep), Blend(deep, Color.Black, 0.35), LinearGradientMode.Vertical))
        {
            g.FillRectangle(foot, 0, 470, Width, Height - 470);
        }

        string family;
        using (var installed = new InstalledFontCollection())
        {
            family = installed.Families.Any(f => f.Name == "Franklin Gothic Medium") ? "Franklin Gothic Medium" : "Segoe UI Semibold";
        }

        var text = title.ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
        var area = new Rectangle(36, 520, Width - 72, 130);
        var points = 34F;
        Font font;
        while (true)
        {
            font = new Font(family, points, FontStyle.Regular, GraphicsUnit.Pixel);
            var size = TextRenderer.MeasureText(text, font, new Size(area.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (size.Height <= area.Height || points <= 20)
            {
                area = new Rectangle(area.X, 650 - size.Height, area.Width, size.Height);
                break;
            }

            font.Dispose();
            points -= 2;
        }

        using (font)
        {
            TextRenderer.DrawText(g, text, font, area, Color.White, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        }

        using var rule = new SolidBrush(accent);
        g.FillRectangle(rule, 36, 662, 44, 4);
        using var small = new Font("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, year.ToString(CultureInfo.InvariantCulture), small, new Point(90, 652), Color.FromArgb(230, 255, 255, 255), TextFormatFlags.NoPrefix);
    }

    private static Color Blend(Color a, Color b, double amount) => Color.FromArgb(
        (int)Math.Round(a.R + ((b.R - a.R) * amount)),
        (int)Math.Round(a.G + ((b.G - a.G) * amount)),
        (int)Math.Round(a.B + ((b.B - a.B) * amount)));

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
