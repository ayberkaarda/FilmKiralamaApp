using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FilmKiralama.Screenshots;

/// <summary>
/// Makes one textless poster picture per invented film with the Gemini image API and keeps them in
/// <c>tools/Screenshots/posters/film-N.jpg</c>. Pictures are made once and committed; the title and year are
/// set over them in code (<see cref="PosterArt.Compose"/>) because image models misspell lettering.
/// The key is read from the GEMINI_API_KEY environment variable and is never written anywhere.
/// </summary>
internal static class PosterGenerator
{
    private const string DefaultModel = "gemini-2.5-flash-image";

    public static string PicturePath(int filmId) =>
        Path.Combine(Program.FindRepositoryRoot(), "tools", "Screenshots", "posters", string.Create(CultureInfo.InvariantCulture, $"film-{filmId}.jpg"));

    public static async Task<int> RunAsync(List<string> options)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Set the GEMINI_API_KEY environment variable first.");
            return 2;
        }

        var force = options.Remove("--force");
        var model = DefaultModel;
        var modelIndex = options.IndexOf("--model");
        if (modelIndex >= 0 && modelIndex + 1 < options.Count)
        {
            model = options[modelIndex + 1];
            options.RemoveRange(modelIndex, 2);
        }

        var only = options.Where(o => int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(o => int.Parse(o, CultureInfo.InvariantCulture)).ToHashSet();
        Directory.CreateDirectory(Path.GetDirectoryName(PicturePath(0))!);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Add("x-goog-api-key", key);
        var failures = 0;
        foreach (var item in new SyntheticCinema().Films)
        {
            var film = item.Film;
            var path = PicturePath(film.Id);
            if ((only.Count > 0 && !only.Contains(film.Id)) || (File.Exists(path) && !force))
            {
                continue;
            }

            try
            {
                var bytes = await GenerateAsync(http, model, Prompt(film.Title, film.Genre, film.Year, film.About)).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                Console.WriteLine($"film-{film.Id}.jpg  {film.Title}");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or JsonException or FormatException)
            {
                failures++;
                Console.Error.WriteLine($"film-{film.Id}: {ex.Message}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static string Prompt(string title, string genre, int year, string? about) =>
        "A cinematic movie poster illustration for an invented " + genre.ToLowerInvariant() + " film from " + year +
        ". Mood and story: " + about + " Vertical 2:3 composition, painterly or photographic key art, strong single focal " +
        "point, rich color, with calm empty space at the bottom third. Absolutely no text, no letters, no numbers, no logos, " +
        "no watermarks, no real people or celebrities, no existing franchise or brand. The film is named \"" + title +
        "\" but the name must not appear in the image.";

    private static async Task<byte[]> GenerateAsync(HttpClient http, string model, string prompt)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }) }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("IMAGE"),
                ["imageConfig"] = new JsonObject { ["aspectRatio"] = "2:3" },
            },
        };

        using var response = await http.PostAsJsonAsync(new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"), body).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {text[..Math.Min(300, text.Length)]}");
        }

        var parts = JsonNode.Parse(text)?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        var data = parts?.Select(p => p?["inlineData"]?["data"]?.GetValue<string>()).FirstOrDefault(d => d is not null);
        return data is null ? throw new InvalidOperationException("The response held no image.") : Convert.FromBase64String(data);
    }
}
