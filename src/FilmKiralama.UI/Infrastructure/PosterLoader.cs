using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FilmKiralama.Business.Validation;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Downloads film posters in the background and shows them in picture boxes. Only the most recent
/// request of a picture box is shown (a slower earlier answer never replaces a newer poster), and the
/// placeholder stays when a download is refused or fails.
/// </summary>
/// <remarks>
/// The addresses come from the database and are fetched by every customer's computer, so a download
/// is limited: http or https only, every address a host name resolves to must be public
/// (<see cref="UrlGuard"/>) and the connection goes to the address that was checked, redirects are
/// followed by hand under the same rules, at most <see cref="MaxBytes"/> of an image content type,
/// within <see cref="Timeout"/>. No proxy, cookies or credentials are used.
/// </remarks>
internal sealed class PosterLoader : IDisposable
{
    /// <summary>Largest poster accepted.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Largest poster size in pixels accepted (a small file can describe a huge bitmap).</summary>
    public const int MaxPixels = 4096 * 4096;

    public const int MaxRedirects = 3;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly Lazy<PosterLoader> DefaultLoader = new(() => new PosterLoader(UrlGuard.IsPublicAddress));

    private readonly Func<Uri, CancellationToken, Task<Image?>> _fetch;
    private readonly HttpClient? _client;
    private readonly ConditionalWeakTable<PictureBox, PosterState> _states = new();

    /// <summary>A loader that connects only to addresses <paramref name="allowAddress"/> accepts.</summary>
    public PosterLoader(Func<IPAddress, bool> allowAddress)
    {
        ArgumentNullException.ThrowIfNull(allowAddress);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            Credentials = null,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, allowAddress, token),
        };
        _client = new HttpClient(handler) { Timeout = Timeout };
        _fetch = FetchAsync;
    }

    /// <summary>A loader with its own download step; for tests.</summary>
    internal PosterLoader(Func<Uri, CancellationToken, Task<Image?>> fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        _fetch = fetch;
    }

    /// <summary>The loader of the application, limited to public addresses.</summary>
    public static PosterLoader Default => DefaultLoader.Value;

    /// <summary>
    /// Shows the placeholder in <paramref name="pictureBox"/> at once and, for an http or https address,
    /// the downloaded poster when it arrives, unless another poster was asked for in the meantime.
    /// Must be called on the UI thread.
    /// </summary>
    public void Show(PictureBox pictureBox, string? imageUrl)
    {
        ArgumentNullException.ThrowIfNull(pictureBox);

        var state = Restart(pictureBox, out var request, out var token);
        SetImage(pictureBox, state, AppAssets.Poster, owned: false);
        if (!InputRules.IsValidImageUrl(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || !UrlGuard.IsAllowedUri(uri))
        {
            return;
        }

        _ = LoadAsync(pictureBox, state, request, uri, token);
    }

    public void Dispose() => _client?.Dispose();

    /// <summary>Empties <paramref name="pictureBox"/>; a download still running for it is dropped.</summary>
    public void Clear(PictureBox pictureBox)
    {
        ArgumentNullException.ThrowIfNull(pictureBox);

        var state = Restart(pictureBox, out _, out _);
        SetImage(pictureBox, state, null, owned: false);
    }

    /// <summary>
    /// Downloads <paramref name="uri"/> under the rules of this loader and decodes it in the background;
    /// <see langword="null"/> when the address, an answer or the image is refused.
    /// </summary>
    internal async Task<Image?> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var client = _client ?? throw new InvalidOperationException("This loader has no HTTP client.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var token = timeout.Token;
        try
        {
            var current = uri;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!UrlGuard.IsAllowedUri(current))
                {
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400)
                {
                    if (response.Headers.Location is not { } location)
                    {
                        return null;
                    }

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!response.IsSuccessStatusCode
                    || mediaType is null
                    || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    || response.Content.Headers.ContentLength > MaxBytes)
                {
                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    var bytes = await ReadAtMostAsync(stream, MaxBytes, token).ConfigureAwait(false);
                    return bytes is null ? null : await Task.Run(() => Decode(bytes), token).ConfigureAwait(false);
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or SocketException)
        {
            return null;
        }
    }

    /// <summary>The image in <paramref name="bytes"/> as a bitmap of its own, or <see langword="null"/>.</summary>
    internal static Image? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return (long)image.Width * image.Height > MaxPixels ? null : new Bitmap(image);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadAtMostAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Resolves the host, refuses it unless every address it has is allowed, and connects to a checked
    /// address itself, so the name cannot resolve differently between the check and the connection.
    /// </summary>
    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, Func<IPAddress, bool> allowAddress, CancellationToken cancellationToken)
    {
        var host = endPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || !addresses.All(allowAddress))
        {
            throw new HttpRequestException("The poster address is not a public address.");
        }

        var address = addresses[0];
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, endPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static void SetImage(PictureBox pictureBox, PosterState state, Image? image, bool owned)
    {
        var previous = state.Owned;
        pictureBox.Image = image;
        state.Owned = owned ? image : null;
        if (previous is not null && !ReferenceEquals(previous, image))
        {
            previous.Dispose();
        }
    }

    private PosterState Restart(PictureBox pictureBox, out int request, out CancellationToken token)
    {
        pictureBox.CancelAsync();
        var state = _states.GetValue(pictureBox, _ => new PosterState());
        state.Pending?.Cancel();
        state.Pending = new CancellationTokenSource();
        token = state.Pending.Token;
        request = state.Requests.Next();
        return state;
    }

    private async Task LoadAsync(PictureBox pictureBox, PosterState state, int request, Uri uri, CancellationToken token)
    {
        Image? image;
        try
        {
            image = await _fetch(uri, token);
        }
#pragma warning disable CA1031 // A poster that cannot be shown keeps the placeholder.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        // Continues on the UI thread: only the latest request of this picture box is shown.
        if (image is null)
        {
            return;
        }

        if (!state.Requests.IsLatest(request) || pictureBox.IsDisposed)
        {
            image.Dispose();
            return;
        }

        SetImage(pictureBox, state, image, owned: true);
    }

    private sealed class PosterState
    {
        public RequestSequence Requests { get; } = new();

        public CancellationTokenSource? Pending { get; set; }

        public Image? Owned { get; set; }
    }
}
