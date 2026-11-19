using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FilmKiralama.UI.Infrastructure;
using FilmKiralama.UI.Tests.Support;
using static FilmKiralama.UI.Tests.Support.StaThread;

namespace FilmKiralama.UI.Tests;

/// <summary>Posters come only from public web addresses, within limits, and only the latest one is shown.</summary>
public sealed class PosterLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::127.0.0.1")]
    [InlineData("64:ff9b::a00:5")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("2001:0:4136:e378::1")]
    [InlineData("ff02::1")]
    public void UrlGuard_RefusesLocalAndReservedAddresses(string address)
    {
        Assert.False(UrlGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    /// <summary>
    /// IPv6 transition forms that carry an IPv4 address are judged by that address: a private, loopback,
    /// link-local or cloud metadata address hidden in NAT64, 6to4, IPv4-compatible, IPv4-mapped or
    /// IPv4-translated form is refused.
    /// </summary>
    [Theory]
    [InlineData("64:ff9b::7f00:1")]          // NAT64, 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]       // NAT64, 169.254.169.254
    [InlineData("64:ff9b::c0a8:101")]        // NAT64, 192.168.1.1
    [InlineData("64:ff9b:1::a00:1")]         // local-use NAT64
    [InlineData("64:ff9b:1:ffff::808:808")]  // local-use NAT64, whatever it carries
    [InlineData("2002:a00:1::")]             // 6to4, 10.0.0.1
    [InlineData("2002:a9fe:a9fe::1")]        // 6to4, 169.254.169.254
    [InlineData("2002:c0a8:101:1::1")]       // 6to4, 192.168.1.1
    [InlineData("::10.0.0.1")]               // IPv4-compatible
    [InlineData("::169.254.169.254")]        // IPv4-compatible
    [InlineData("::ffff:10.0.0.1")]          // IPv4-mapped
    [InlineData("::ffff:172.16.5.4")]        // IPv4-mapped
    [InlineData("::ffff:0:a00:1")]           // IPv4-translated (SIIT), 10.0.0.1
    [InlineData("::ffff:0:a9fe:a9fe")]       // IPv4-translated (SIIT), 169.254.169.254
    [InlineData("2001:db8::1")]              // documentation
    [InlineData("100::1")]                   // discard-only
    public void UrlGuard_RefusesPrivateAddressesInIPv6TransitionForms(string address)
    {
        Assert.False(UrlGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2002:808:808::1")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("::ffff:0:808:808")]
    [InlineData("::8.8.8.8")]
    public void UrlGuard_AllowsPublicAddresses(string address)
    {
        Assert.True(UrlGuard.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("https://example.com/a.png", true)]
    [InlineData("http://example.com/a.png", true)]
    [InlineData("ftp://example.com/a.png", false)]
    [InlineData("file:///C:/a.png", false)]
    [InlineData("https://kullanici:sifre@example.com/a.png", false)]
    public void UrlGuard_AllowsOnlyWebAddressesWithoutUserInformation(string address, bool allowed)
    {
        Assert.Equal(allowed, UrlGuard.IsAllowedUri(new Uri(address)));
    }

    /// <summary>The application's loader never connects to the local computer or the cloud metadata address.</summary>
    [Fact]
    public async Task DefaultLoader_RefusesLoopbackAndMetadataWithoutConnecting()
    {
        using var server = new StubServer(_ => Response("image/png", Png()));
        using var loader = new PosterLoader(UrlGuard.IsPublicAddress);

        Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/a.png"), Ct));
        Assert.Null(await loader.FetchAsync(new Uri($"http://localhost:{server.Port}/a.png"), Ct));
        Assert.Null(await loader.FetchAsync(new Uri($"http://[::ffff:127.0.0.1]:{server.Port}/a.png"), Ct));
        Assert.Null(await loader.FetchAsync(new Uri("http://169.254.169.254/latest/meta-data/"), Ct));

        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task Fetch_AnImage_IsDecoded()
    {
        using var server = new StubServer(_ => Response("image/png", Png()));
        using var loader = LoopbackLoader();

        using var image = await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/a.png"), Ct);

        Assert.NotNull(image);
        Assert.Equal(new Size(4, 3), image.Size);
    }

    [Fact]
    public async Task Fetch_NotAnImageOrTooLarge_IsRefused()
    {
        var big = new byte[PosterLoader.MaxBytes + 1];
        using var server = new StubServer(path => path switch
        {
            "/sayfa" => Response("text/html", Encoding.UTF8.GetBytes("<html></html>")),
            "/buyuk" => Response("image/png", big),
            "/bozuk" => Response("image/png", [1, 2, 3]),
            _ => Response("image/png", Png(), status: "404 Not Found"),
        });
        using var loader = LoopbackLoader();

        foreach (var path in new[] { "/sayfa", "/buyuk", "/bozuk", "/yok" })
        {
            Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}{path}"), Ct));
        }
    }

    /// <summary>A redirect is followed under the same rules: not to a private address or another scheme.</summary>
    [Fact]
    public async Task Fetch_RedirectToAPrivateAddressOrAFile_IsRefused()
    {
        using var server = new StubServer(path => path switch
        {
            "/ozel" => Redirect("http://10.0.0.5/a.png"),
            "/dosya" => Redirect("file:///C:/Windows/win.ini"),
            "/dongu" => Redirect("/dongu"),
            "/yerel" => Redirect("/a.png"),
            _ => Response("image/png", Png()),
        });
        using var loader = LoopbackLoader();

        Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/ozel"), Ct));
        Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/dosya"), Ct));
        Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/dongu"), Ct));
        using var followed = await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/yerel"), Ct);
        Assert.NotNull(followed);
    }

    /// <summary>Answers arriving out of order: the newer poster stays, the stale one is dropped.</summary>
    [Fact]
    public void Show_TwoRequestsOnOnePictureBox_TheEarlierAnswerNeverReplacesTheLater() => Run(() =>
    {
        var fetches = new ControlledFetch();
        using var loader = new PosterLoader(fetches.FetchAsync);
        using var picture = new PictureBox();
        using var first = new Bitmap(2, 2);
        using var second = new Bitmap(3, 3);

        loader.Show(picture, "https://example.com/a.png");
        loader.Show(picture, "https://example.com/b.png");
        Assert.Same(AppAssets.Poster, picture.Image);

        fetches.Complete("https://example.com/b.png", second);
        fetches.Complete("https://example.com/a.png", first);

        Assert.Same(second, picture.Image);
    });

    [Fact]
    public void Show_LocalPath_StartsNoDownload() => Run(() =>
    {
        var fetches = new ControlledFetch();
        using var loader = new PosterLoader(fetches.FetchAsync);
        using var picture = new PictureBox();

        loader.Show(picture, "\\\\sunucu\\pay\\afis.png");
        loader.Show(picture, "file:///C:/afis.png");

        Assert.Empty(fetches.Requested);
        Assert.Same(AppAssets.Poster, picture.Image);
    });

    /// <summary>Selecting a film while the previous film's slow poster loads shows the new film's poster.</summary>
    [Fact]
    public void AdminPanel_SelectingAnotherFilmWhileAPosterLoads_ShowsTheNewPoster() => Run(() =>
    {
        var films = new ControlledFilmService();
        var fetches = new ControlledFetch();
        using var loader = new PosterLoader(fetches.FetchAsync);
        using var form = new AdminPanel(films, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts(), Posters = loader };
        var a = ControlledFilmService.Film(1, "Sisli Liman", "Gerilim");
        a.ImageUrl = "https://example.com/a.png";
        var b = ControlledFilmService.Film(2, "Son Vapur", "Dram");
        b.ImageUrl = "https://example.com/b.png";
        using var posterA = new Bitmap(2, 2);
        using var posterB = new Bitmap(3, 3);

        form.SelectFilm(a);
        form.SelectFilm(b);
        fetches.Complete(b.ImageUrl, posterB);
        fetches.Complete(a.ImageUrl, posterA);

        Assert.Same(posterB, Find<PictureBox>(form, "pictureBox1").Image);
    });

    public static TheoryData<string> Formats => ["png", "jpeg", "gif", "bmp"];

    [Theory]
    [MemberData(nameof(Formats))]
    public void ImageHeader_ReadsTheDeclaredSizeOfEveryPosterFormat(string format)
    {
        var bytes = Encode(5, 7, format switch
        {
            "png" => ImageFormat.Png,
            "jpeg" => ImageFormat.Jpeg,
            "gif" => ImageFormat.Gif,
            _ => ImageFormat.Bmp,
        });

        Assert.True(ImageHeader.TryReadSize(bytes, out var width, out var height));
        Assert.Equal((5, 7), (width, height));
        Assert.Equal(PosterDecodeResult.Decoded, PosterLoader.TryDecode(bytes, out var image));
        using (image)
        {
            Assert.Equal(new Size(5, 7), image!.Size);
        }
    }

    [Theory]
    [InlineData(4096, 4096, true)]
    [InlineData(1, 1, true)]
    [InlineData(4097, 1, false)]
    [InlineData(1, 4097, false)]
    [InlineData(0, 10, false)]
    [InlineData(10, -1, false)]
    public void FitsLimits_AllowsAtMost4096PixelsASideAnd16Megapixels(int width, int height, bool allowed)
    {
        Assert.Equal(allowed, PosterLoader.FitsLimits(width, height));
        Assert.Equal(4096L * 4096, PosterLoader.MaxPixels);
    }

    /// <summary>
    /// A PNG of about a hundred kilobytes that declares 30000 x 30000 pixels (3.6 GB once decoded) is
    /// refused from its header, before a single pixel is decoded, also when it is downloaded.
    /// </summary>
    [Fact]
    public async Task Decode_HugeDeclaredSize_IsRefusedFromTheHeaderBeforeDecoding()
    {
        var bomb = OneBitPng(30_000, 30_000);
        Assert.True(bomb.Length < PosterLoader.MaxBytes, "The test image has to pass the download size limit.");

        var watch = Stopwatch.StartNew();
        Assert.Equal(PosterDecodeResult.TooLarge, PosterLoader.TryDecode(bomb, out var image));
        Assert.Null(image);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "The image was decoded before it was refused.");

        using var server = new StubServer(_ => Response("image/png", bomb));
        using var loader = LoopbackLoader();
        Assert.Null(await loader.FetchAsync(new Uri($"http://127.0.0.1:{server.Port}/bomba.png"), Ct));
        Assert.Equal(1, server.Requests);
    }

    /// <summary>
    /// A large poster is kept only at the size a card can show, so the memory a page of cards holds does
    /// not grow with the size of the files; the shape is kept and a small poster is not enlarged.
    /// </summary>
    [Theory]
    [InlineData(4096, 4096, 480, 480)]
    [InlineData(2000, 3000, 480, 720)]
    [InlineData(1000, 4000, 180, 720)]
    [InlineData(4000, 100, 480, 12)]
    [InlineData(480, 720, 480, 720)]
    [InlineData(120, 160, 120, 160)]
    public void Decode_LargePoster_IsKeptAtMostAtTheShownSize(int width, int height, int keptWidth, int keptHeight)
    {
        var bytes = OneBitPng(width, height);

        Assert.Equal(PosterDecodeResult.Decoded, PosterLoader.TryDecode(bytes, out var image));
        using (image)
        {
            Assert.Equal(new Size(keptWidth, keptHeight), image!.Size);
            Assert.True(image.Width <= PosterLoader.MaxShownWidth && image.Height <= PosterLoader.MaxShownHeight);
        }
    }

    /// <summary>A GIF frame may be larger than the screen the file declares; every frame is checked.</summary>
    [Fact]
    public void Decode_GifFrameLargerThanItsScreen_IsRefused()
    {
        byte[] gif =
        [
            .. "GIF89a"u8, 10, 0, 10, 0, 0, 0, 0,          // 10 x 10 screen, no color table
            0x2C, 0, 0, 0, 0, 0x30, 0x75, 10, 0, 0,      // a 30000 x 10 frame
            2, 1, 0, 0, 0x3B,                             // code size, one data block, end, trailer
        ];

        Assert.True(ImageHeader.TryReadSize(gif, out var width, out _));
        Assert.Equal(30_000, width);
        Assert.Equal(PosterDecodeResult.TooLarge, PosterLoader.TryDecode(gif, out _));
    }

    [Fact]
    public void Decode_UnknownOrCutFormats_AreRefusedWithoutDecoding()
    {
        var png = Encode(4, 3, ImageFormat.Png);

        Assert.Equal(PosterDecodeResult.UnknownFormat, PosterLoader.TryDecode([.. "RIFF\0\0\0\0WEBPVP8 "u8], out _));
        Assert.Equal(PosterDecodeResult.UnknownFormat, PosterLoader.TryDecode(Encode(4, 3, ImageFormat.Tiff), out _));
        Assert.Equal(PosterDecodeResult.UnknownFormat, PosterLoader.TryDecode(png[..20], out _));
        Assert.Equal(PosterDecodeResult.UnknownFormat, PosterLoader.TryDecode([0xFF, 0xD8, 0xFF, 0xD9], out _));
    }

    /// <summary>Many cards showing posters at once: no more than three downloads run at the same time.</summary>
    [Fact]
    public void Show_ManyPictureBoxes_DownloadAtMostThreePostersAtOnce() => Run(() =>
    {
        var fetches = new ControlledFetch();
        using var loader = new PosterLoader(fetches.FetchAsync);
        var pictures = Enumerable.Range(0, 6).Select(_ => new PictureBox()).ToList();
        using var poster = new Bitmap(2, 2);
        try
        {
            for (var i = 0; i < pictures.Count; i++)
            {
                loader.Show(pictures[i], $"https://example.com/{i}.png");
            }

            Assert.Equal(PosterLoader.MaxConcurrentDownloads, fetches.Requested.Count);

            fetches.Complete("https://example.com/0.png", new Bitmap(poster));
            Assert.Equal(PosterLoader.MaxConcurrentDownloads + 1, fetches.Requested.Count);

            // A box that asks for another poster gives up its waiting turn.
            loader.Clear(pictures[4]);
            fetches.Complete("https://example.com/1.png", new Bitmap(poster));
            Assert.Equal(
                ["https://example.com/0.png", "https://example.com/1.png", "https://example.com/2.png", "https://example.com/3.png", "https://example.com/5.png"],
                fetches.Requested);
        }
        finally
        {
            pictures.ForEach(p => p.Dispose());
        }
    });

    private static byte[] Encode(int width, int height, ImageFormat format)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }

    /// <summary>A valid, all black, one bit grayscale PNG of the given size; it compresses to almost nothing.</summary>
    private static byte[] OneBitPng(int width, int height)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // Bit depth; color type 0 (gray), default compression, filter and interlace.
        Chunk(file, "IHDR", header);

        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + ((width + 7) / 8)];
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        Chunk(file, "IDAT", data.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        file.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        file.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static PosterLoader LoopbackLoader() => new(IPAddress.IsLoopback);

    private static byte[] Png()
    {
        using var bitmap = new Bitmap(4, 3);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] Response(string contentType, byte[] body, string status = "200 OK")
    {
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        return [.. head, .. body];
    }

    private static byte[] Redirect(string location) =>
        Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    /// <summary>Downloads that the test completes by hand, in any order.</summary>
    private sealed class ControlledFetch
    {
        private readonly Dictionary<string, TaskCompletionSource<Image?>> _pending = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public Task<Image?> FetchAsync(Uri uri, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<Image?>();
            _pending[uri.AbsoluteUri] = source;
            Requested.Add(uri.AbsoluteUri);
            return source.Task;
        }

        public void Complete(string address, Image image) => _pending[address].SetResult(image);
    }

    /// <summary>A minimal HTTP server on the loopback address that answers each request by its path.</summary>
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Func<string, byte[]> _answer;
        private readonly CancellationTokenSource _stop = new();
        private int _requests;

        public StubServer(Func<string, byte[]> answer)
        {
            _answer = answer;
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Requests => Volatile.Read(ref _requests);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
#pragma warning disable CA1031 // The server stops with the test.
                catch (Exception)
#pragma warning restore CA1031
                {
                    return;
                }

                Interlocked.Increment(ref _requests);
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var header = new StringBuilder();
                        var buffer = new byte[1];
                        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(buffer) == 1)
                        {
                            header.Append((char)buffer[0]);
                        }

                        var path = header.ToString().Split(' ') is { Length: > 1 } parts ? parts[1] : "/";
                        await stream.WriteAsync(_answer(path));
                    }
                    catch (IOException)
                    {
                        // The client gave up on the answer (for example a refused, too large poster).
                    }
                }
            }
        }
    }
}
