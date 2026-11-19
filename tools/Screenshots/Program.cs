using System.Drawing.Imaging;

namespace FilmKiralama.Screenshots;

/// <summary>
/// Usage: <c>dotnet run --project tools/Screenshots -- icons</c>. "icons" rasterizes assets/logo.svg into
/// assets/app.ico and assets/logo-256.png, and assets/poster.svg into assets/poster.png. No window is shown
/// and the focus is never taken.
/// </summary>
internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "icons";
        if (command != "icons")
        {
            Console.Error.WriteLine("Usage: FilmKiralama.Screenshots icons");
            return 2;
        }

        WriteIcons(FindRepositoryRoot());
        return 0;
    }

    private static void WriteIcons(string root)
    {
        var svg = Path.Combine(root, "assets", "logo.svg");
        var frames = IconSizes.Select(size => SvgRasterizer.Render(svg, size)).ToList();
        try
        {
            IconWriter.Write(Path.Combine(root, "assets", "app.ico"), frames);
            frames[^1].Save(Path.Combine(root, "assets", "logo-256.png"), ImageFormat.Png);
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }

        // The poster placeholder at the largest size a poster is kept at (PosterLoader.MaxShownWidth x MaxShownHeight).
        using (var poster = SvgRasterizer.Render(Path.Combine(root, "assets", "poster.svg"), 480, 720))
        {
            poster.Save(Path.Combine(root, "assets", "poster.png"), ImageFormat.Png);
        }

        Console.WriteLine("assets/app.ico, assets/logo-256.png, assets/poster.png");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilmKiralama.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("FilmKiralama.sln was not found above the tool's folder.");
    }
}
