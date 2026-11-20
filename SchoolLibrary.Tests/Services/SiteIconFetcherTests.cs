using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SchoolLibrary.Services.SiteIcons;

namespace SchoolLibrary.Tests.Services;

public class SiteIconFetcherTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Ico = { 0x00, 0x00, 0x01, 0x00, 1, 0, 16, 16 };
    private static readonly byte[] Svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // Answers by full address; anything else is 404. "Redirects" maps an address to the final one.
    private sealed class FakeSite : HttpMessageHandler
    {
        public Dictionary<string, (string ContentType, byte[] Body)> Pages { get; } = new();
        public Dictionary<string, string> Redirects { get; } = new();
        public List<string> Requested { get; } = new();
        public bool Fail { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            if (Fail) throw new HttpRequestException("down");

            if (Redirects.TryGetValue(url, out var final))
            {
                url = final;
                request = new HttpRequestMessage(request.Method, final);
            }

            if (!Pages.TryGetValue(url, out var page))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }

            var content = new ByteArrayContent(page.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue(page.ContentType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    private static SiteIconFetcher Fetcher(FakeSite site) => new(new FakeFactory(site));

    private static (string, byte[]) Html(string head) => ("text/html", Encoding.UTF8.GetBytes($"<html><head>{head}</head><body></body></html>"));

    [Fact]
    public async Task FetchAsync_PrefersTheAppleTouchIcon()
    {
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = Html("""
            <link rel="icon" href="/small.png" sizes="16x16">
            <link rel="apple-touch-icon" href="/touch.png">
            """);
        site.Pages["https://lib.bg/touch.png"] = ("image/png", Png);
        site.Pages["https://lib.bg/small.png"] = ("image/png", Ico);

        var icon = await Fetcher(site).FetchAsync("https://lib.bg/");

        Assert.Equal("image/png", icon!.ContentType);
        Assert.Equal(Png, icon.Content);
    }

    [Fact]
    public async Task FetchAsync_WithoutIconLinks_UsesFaviconIco()
    {
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = Html("<title>Библиотека</title>");
        site.Pages["https://lib.bg/favicon.ico"] = ("image/x-icon", Ico);

        var icon = await Fetcher(site).FetchAsync("https://lib.bg");

        Assert.Equal("image/x-icon", icon!.ContentType);
    }

    [Fact]
    public async Task FetchAsync_ABrokenIconLink_FallsBackToTheNextOne()
    {
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = Html("""<link rel="apple-touch-icon" href="/missing.png"><link rel="shortcut icon" href="/fav.ico">""");
        site.Pages["https://lib.bg/fav.ico"] = ("image/x-icon", Ico);

        var icon = await Fetcher(site).FetchAsync("https://lib.bg/");

        Assert.Equal(Ico, icon!.Content);
    }

    [Fact]
    public async Task FetchAsync_ResolvesIconsAgainstTheAddressAfterARedirect()
    {
        var site = new FakeSite();
        site.Redirects["https://lib.bg/"] = "https://www.lib.bg/start/";
        site.Pages["https://www.lib.bg/start/"] = Html("""<link rel="icon" href="img/i.png">""");
        site.Pages["https://www.lib.bg/start/img/i.png"] = ("image/png", Png);

        var icon = await Fetcher(site).FetchAsync("https://lib.bg/");

        Assert.NotNull(icon);
        Assert.Contains("https://www.lib.bg/start/img/i.png", site.Requested);
    }

    [Fact]
    public async Task FetchAsync_RefusesSvgEvenWhenItIsTheOnlyIcon()
    {
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = Html("""<link rel="icon" href="/fav.png">""");
        // An SVG served under a .png name is recognized by its bytes
        site.Pages["https://lib.bg/fav.png"] = ("image/png", Svg);
        site.Pages["https://lib.bg/favicon.ico"] = ("image/svg+xml", Svg);

        Assert.Null(await Fetcher(site).FetchAsync("https://lib.bg/"));
    }

    [Fact]
    public async Task FetchAsync_RefusesAnIconLargerThanTheLimit()
    {
        var big = new byte[SiteIconFetcher.MaxIconBytes + 1];
        Png.CopyTo(big, 0);
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = Html("");
        site.Pages["https://lib.bg/favicon.ico"] = ("image/png", big);

        Assert.Null(await Fetcher(site).FetchAsync("https://lib.bg/"));
    }

    [Fact]
    public async Task FetchAsync_AnUnreachableSite_ReturnsNull()
    {
        var site = new FakeSite { Fail = true };

        Assert.Null(await Fetcher(site).FetchAsync("https://lib.bg/"));
    }

    [Fact]
    public async Task FetchAsync_APageThatIsNotHtml_StillTriesFaviconIco()
    {
        var site = new FakeSite();
        site.Pages["https://lib.bg/"] = ("application/json", Encoding.UTF8.GetBytes("""{"rel":"icon"}"""));
        site.Pages["https://lib.bg/favicon.ico"] = ("image/x-icon", Ico);

        Assert.NotNull(await Fetcher(site).FetchAsync("https://lib.bg/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://lib.bg/")]
    public async Task FetchAsync_ANonWebAddress_SendsNoRequest(string url)
    {
        var site = new FakeSite();

        Assert.Null(await Fetcher(site).FetchAsync(url));
        Assert.Empty(site.Requested);
    }

    [Fact]
    public void FindIconLinks_OrdersByDeclaredSize_AndSkipsSvgAndNonWebLinks()
    {
        const string html = """
            <link rel="stylesheet" href="/site.css">
            <link rel="icon" type="image/svg+xml" href="/logo">
            <link rel="icon" href="/logo.svg">
            <link rel="icon" href="javascript:alert(1)">
            <link rel="icon" href="data:image/png;base64,AAAA">
            <link rel='icon' sizes='32x32' href='/32.png'>
            <link href="/192.png" sizes="16x16 192x192" rel="icon">
            <LINK REL="Shortcut Icon" HREF="/fav.ico?v=1&amp;x=2">
            """;

        var links = SiteIconFetcher.FindIconLinks(html, new Uri("https://lib.bg/a/"));

        Assert.Equal(new[] { "https://lib.bg/192.png", "https://lib.bg/32.png", "https://lib.bg/fav.ico?v=1&x=2" },
            links.Select(l => l.ToString()));
    }

    [Fact]
    public void DetectImageType_RecognizesTheSupportedFormats_ByTheirBytes()
    {
        Assert.Equal("image/png", SiteIconFetcher.DetectImageType(Png));
        Assert.Equal("image/x-icon", SiteIconFetcher.DetectImageType(Ico));
        Assert.Equal("image/jpeg", SiteIconFetcher.DetectImageType(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.Equal("image/gif", SiteIconFetcher.DetectImageType("GIF89a.."u8));
        Assert.Equal("image/webp", SiteIconFetcher.DetectImageType("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(SiteIconFetcher.DetectImageType(Svg));
        Assert.Null(SiteIconFetcher.DetectImageType("<html>"u8));
        Assert.Null(SiteIconFetcher.DetectImageType(ReadOnlySpan<byte>.Empty));
    }
}
