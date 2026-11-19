using System.Drawing.Imaging;
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

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2002:808:808::1")]
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
