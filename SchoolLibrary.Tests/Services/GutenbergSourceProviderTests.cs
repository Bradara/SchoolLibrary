using System.Net;
using System.Text;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class GutenbergSourceProviderTests
{
    // The shape of the OPDS search feed: navigation entries and book entries side by side
    private const string Feed = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Search Results</title>
          <entry>
            <title>Books by Language</title>
            <id>https://www.gutenberg.org/ebooks/search.opds/?sort_order=downloads</id>
            <content type="text">Navigation</content>
          </entry>
          <entry>
            <title>Евгений Онегин</title>
            <id>https://www.gutenberg.org/ebooks/23997.opds</id>
            <content type="text">Александр Сергеевич Пушкин</content>
          </entry>
          <entry>
            <title>Anonymous Tale</title>
            <id>https://www.gutenberg.org/ebooks/555.opds</id>
          </entry>
          <entry>
            <title>   </title>
            <id>https://www.gutenberg.org/ebooks/777.opds</id>
          </entry>
        </feed>
        """;

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        public Uri? LastUri { get; private set; }

        public FakeHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/atom+xml") });
        }
    }

    private static (GutenbergSourceProvider Provider, FakeHandler Handler) Create(string body = Feed, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(body, status);
        return (new GutenbergSourceProvider(new FakeFactory(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_TurnsBookEntriesIntoCandidates_AndIgnoresNavigationEntries()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("пушкин", 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        var book = result[0];
        Assert.Equal("Евгений Онегин", book.Title);
        Assert.Equal("https://www.gutenberg.org/ebooks/23997", book.Url);
        Assert.Equal("Александр Сергеевич Пушкин", book.Author);
        Assert.Equal("Обществено достояние", book.LicenseName);
        Assert.Contains("Александр Сергеевич Пушкин", book.Description);
    }

    [Fact]
    public async Task SearchAsync_ABookWithoutAnAuthor_IsCreditedToTheProject()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("tale", 10, CancellationToken.None);

        Assert.Equal("Проект Гутенберг", result[1].Author);
        Assert.Equal("Книга в Проект Гутенберг.", result[1].Description);
    }

    [Fact]
    public async Task SearchAsync_HonoursTheLimit()
    {
        var (provider, _) = Create();

        Assert.Single(await provider.SearchAsync("book", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_AsksTheFixedHostWithTheEscapedTopic()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("war & peace", 10, CancellationToken.None);

        Assert.Equal("www.gutenberg.org", handler.LastUri!.Host);
        Assert.Contains("query=war%20peace", handler.LastUri.AbsoluteUri);
    }

    [Fact]
    public async Task SearchAsync_CutsAVeryLongTitle()
    {
        var feed = $"""<feed xmlns="http://www.w3.org/2005/Atom"><entry><title>{new string('я', 300)}</title><id>https://www.gutenberg.org/ebooks/1.opds</id></entry></feed>""";
        var (provider, _) = Create(feed);

        var result = await provider.SearchAsync("book", 10, CancellationToken.None);

        Assert.Equal(100, result[0].Title.Length);
    }

    [Fact]
    public async Task SearchAsync_AFeedWithoutBooks_ReturnsNothing()
    {
        var (provider, _) = Create("""<feed xmlns="http://www.w3.org/2005/Atom"></feed>""");

        Assert.Empty(await provider.SearchAsync("book", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_WhenGutenbergIsDown_Throws()
    {
        var (provider, _) = Create("<html>maintenance</html>", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("book", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_WhenTheAnswerIsNotAFeed_Throws()
    {
        var (provider, _) = Create("<html><body>Temporarily down");

        await Assert.ThrowsAnyAsync<System.Xml.XmlException>(() => provider.SearchAsync("book", 10, CancellationToken.None));
    }

    [Fact]
    public void TheKeyAndNameAreSet()
    {
        var (provider, _) = Create();

        Assert.Equal("gutenberg", provider.Key);
        Assert.Contains("Гутенберг", provider.DisplayName);
    }
}
