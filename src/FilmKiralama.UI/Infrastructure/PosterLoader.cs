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
/// within <see cref="Timeout"/>. No proxy, cookies or credentials are used. The pixel size the file
/// declares is read from its header and checked against <see cref="MaxDimension"/> and
/// <see cref="MaxPixels"/> before any pixel is decoded, and at most <see cref="MaxConcurrentDownloads"/>
/// posters are downloaded and decoded at the same time. A decoded poster is kept only at the size a card
/// shows (at most <see cref="MaxShownWidth"/> x <see cref="MaxShownHeight"/>), so a full size bitmap lives
/// only while it is scaled down, and a page of cards pointing at huge images holds about 1.4 MB a card.
/// </remarks>
internal sealed class PosterLoader : IDisposable
{
    /// <summary>Largest poster accepted.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>Largest width or height in pixels accepted (a small file can describe a huge bitmap).</summary>
    public const int MaxDimension = 4096;

    /// <summary>Largest number of pixels accepted (16 MP, about 64 MB once decoded).</summary>
    public const long MaxPixels = (long)MaxDimension * MaxDimension;

    /// <summary>Widest poster kept after decoding; twice the card's poster width of 240.</summary>
    public const int MaxShownWidth = 480;

    /// <summary>Tallest poster kept after decoding (a 2:3 poster at <see cref="MaxShownWidth"/>).</summary>
    public const int MaxShownHeight = 720;

    /// <summary>Posters downloaded and decoded at the same time by one loader.</summary>
    public const int MaxConcurrentDownloads = 3;

    public const int MaxRedirects = 3;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly Lazy<PosterLoader> DefaultLoader = new(() => new PosterLoader(UrlGuard.IsPublicAddress));

    private readonly Func<Uri, CancellationToken, Task<Image?>> _fetch;
    private readonly HttpClient? _client;
    private readonly ConditionalWeakTable<PictureBox, PosterState> _states = new();
    private readonly DownloadSlots _downloads = new(MaxConcurrentDownloads);

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

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!IsAllowedRedirect(current, next))
                    {
                        return null;
                    }

                    current = next;
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

    /// <summary>
    /// Whether a redirect from <paramref name="from"/> to <paramref name="to"/> may be followed: the new
    /// address must pass the same rules, and an https address never leads to plain http.
    /// </summary>
    internal static bool IsAllowedRedirect(Uri from, Uri to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return UrlGuard.IsAllowedUri(to)
            && !(from.Scheme == Uri.UriSchemeHttps && to.Scheme != Uri.UriSchemeHttps);
    }

    /// <summary>The image in <paramref name="bytes"/> as a bitmap of its own, or <see langword="null"/>.</summary>
    internal static Image? Decode(byte[] bytes) => TryDecode(bytes, out var image) == PosterDecodeResult.Decoded ? image : null;

    /// <summary>
    /// Decodes <paramref name="bytes"/> into a bitmap of its own. The size the header declares is checked
    /// first and an image beyond the limits is refused without decoding a single pixel; the image is then
    /// opened lazily, its size checked again and only then are the pixels decoded.
    /// </summary>
    internal static PosterDecodeResult TryDecode(byte[] bytes, out Image? image)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        image = null;
        if (!ImageHeader.TryReadSize(bytes, out var width, out var height))
        {
            return PosterDecodeResult.UnknownFormat;
        }

        if (!FitsLimits(width, height))
        {
            return PosterDecodeResult.TooLarge;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);

            // Without validation GDI+ reads the header only; the pixels are decoded by the copy below.
            using var opened = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if (!FitsLimits(opened.Width, opened.Height))
            {
                return PosterDecodeResult.TooLarge;
            }

            image = Shrink(opened);
            return PosterDecodeResult.Decoded;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return PosterDecodeResult.Invalid;
        }
    }

    /// <summary>
    /// The size <paramref name="width"/> x <paramref name="height"/> scaled down, keeping its shape, to fit
    /// <see cref="MaxShownWidth"/> x <see cref="MaxShownHeight"/>; a size that fits is kept.
    /// </summary>
    internal static Size ShownSize(int width, int height)
    {
        var scale = Math.Min(1d, Math.Min((double)MaxShownWidth / width, (double)MaxShownHeight / height));
        return new Size(
            Math.Clamp((int)Math.Round(width * scale), 1, MaxShownWidth),
            Math.Clamp((int)Math.Round(height * scale), 1, MaxShownHeight));
    }

    /// <summary>A bitmap of its own of <paramref name="opened"/>, decoded and scaled down to <see cref="ShownSize"/>.</summary>
    private static Bitmap Shrink(Image opened)
    {
        var size = ShownSize(opened.Width, opened.Height);
        if (size == opened.Size)
        {
            return new Bitmap(opened);
        }

        var shown = new Bitmap(size.Width, size.Height);
        try
        {
            using var graphics = Graphics.FromImage(shown);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            using var wrap = new System.Drawing.Imaging.ImageAttributes();
            wrap.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
            graphics.DrawImage(opened, new Rectangle(Point.Empty, size), 0, 0, opened.Width, opened.Height, GraphicsUnit.Pixel, wrap);
            return shown;
        }
        catch
        {
            shown.Dispose();
            throw;
        }
    }

    /// <summary>Whether an image of <paramref name="width"/> x <paramref name="height"/> pixels may be decoded.</summary>
    internal static bool FitsLimits(int width, int height) =>
        width is > 0 and <= MaxDimension
        && height is > 0 and <= MaxDimension
        && (long)width * height <= MaxPixels;

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
        var entered = false;
        try
        {
            // At most MaxConcurrentDownloads downloads and decodes at a time; a request replaced while it
            // waits gives up its turn.
            await _downloads.EnterAsync(token);
            entered = true;
            image = await _fetch(uri, token);
        }
#pragma warning disable CA1031 // A poster that cannot be shown keeps the placeholder.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }
        finally
        {
            if (entered)
            {
                _downloads.Leave();
            }
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

    /// <summary>
    /// At most a given number of downloads at a time, the others waiting in order. Used on the UI thread
    /// only, like the loader itself, so a freed slot is handed to the next waiting request at once, on
    /// that thread.
    /// </summary>
    private sealed class DownloadSlots(int count)
    {
        private readonly Queue<TaskCompletionSource> _waiting = new();
        private int _taken;

        /// <summary>Takes a slot, waiting for one when all are taken; a canceled wait takes none.</summary>
        public Task EnterAsync(CancellationToken token)
        {
            if (_taken < count)
            {
                _taken++;
                return Task.CompletedTask;
            }

            var turn = new TaskCompletionSource();
            token.Register(() => turn.TrySetCanceled(token));
            _waiting.Enqueue(turn);
            return turn.Task;
        }

        /// <summary>Hands the slot to the first request still waiting, or frees it.</summary>
        public void Leave()
        {
            while (_waiting.TryDequeue(out var next))
            {
                if (next.TrySetResult())
                {
                    return;
                }
            }

            _taken--;
        }
    }

    private sealed class PosterState
    {
        public RequestSequence Requests { get; } = new();

        public CancellationTokenSource? Pending { get; set; }

        public Image? Owned { get; set; }
    }
}

/// <summary>Outcome of <see cref="PosterLoader.TryDecode"/>.</summary>
internal enum PosterDecodeResult
{
    Decoded = 0,

    /// <summary>Not a PNG, JPEG, GIF or BMP file, or its header is cut short; nothing was decoded.</summary>
    UnknownFormat = 1,

    /// <summary>The declared size is beyond the limits; refused before any pixel was decoded.</summary>
    TooLarge = 2,

    /// <summary>The image data is damaged.</summary>
    Invalid = 3,
}
