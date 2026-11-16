using System.Net;
using System.Text;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class WikimediaSourceProviderTests
{
    private static readonly WikimediaWiki Wiki = new("wikipedia-bg", "bg.wikipedia.org", "Уикипедия (български)", "Сътрудници на Уикипедия");

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (WikimediaSourceProvider Provider, FakeHandler Handler) Create(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(_ => Json(json, status));
        return (new WikimediaSourceProvider(new HttpClient(handler), Wiki), handler);
    }

    [Fact]
    public async Task SearchAsync_TurnsEveryHitIntoACandidateWithThePageLinkAndTheWikiLicense()
    {
        var (provider, _) = Create("""
            {"query":{"search":[
              {"title":"Фотосинтеза","snippet":"ключова роля в <span class=\"searchmatch\">фотосинтеза</span> &amp; дишане"},
              {"title":"Васкуларни растения","snippet":""}]}}
            """);

        var result = await provider.SearchAsync("фотосинтеза", 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        var first = result[0];
        Assert.Equal("Фотосинтеза", first.Title);
        Assert.Equal("https://bg.wikipedia.org/wiki/%D0%A4%D0%BE%D1%82%D0%BE%D1%81%D0%B8%D0%BD%D1%82%D0%B5%D0%B7%D0%B0", first.Url);
        Assert.Equal("CC BY-SA 4.0", first.LicenseName);
        Assert.Equal("Сътрудници на Уикипедия", first.Author);
        Assert.Equal("Уикипедия (български)", first.SourceName);
        Assert.Equal("ключова роля в фотосинтеза & дишане", first.Description);
        Assert.Equal(string.Empty, result[1].Description);
    }

    [Fact]
    public async Task SearchAsync_ReplacesSpacesInTheTitleWithUnderscoresInTheLink()
    {
        var (provider, _) = Create("""{"query":{"search":[{"title":"Втора световна война","snippet":"x"}]}}""");

        var result = await provider.SearchAsync("война", 10, CancellationToken.None);

        Assert.EndsWith("%D0%92%D1%82%D0%BE%D1%80%D0%B0_%D1%81%D0%B2%D0%B5%D1%82%D0%BE%D0%B2%D0%BD%D0%B0_%D0%B2%D0%BE%D0%B9%D0%BD%D0%B0", result[0].Url);
    }

    [Fact]
    public async Task SearchAsync_AsksTheFixedHostWithTheEscapedTopicAndLimit()
    {
        var (provider, handler) = Create("""{"query":{"search":[]}}""");

        await provider.SearchAsync("a&b=c d", 7, CancellationToken.None);

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("bg.wikipedia.org", uri.Host);
        Assert.Equal("https", uri.Scheme);
        Assert.Contains("srsearch=a%26b%3Dc%20d", uri.AbsoluteUri);
        Assert.Contains("srlimit=7", uri.AbsoluteUri);
    }

    [Fact]
    public async Task SearchAsync_CutsALongSnippet()
    {
        var (provider, _) = Create("{\"query\":{\"search\":[{\"title\":\"A\",\"snippet\":\"" + new string('я', 500) + "\"}]}}");

        var result = await provider.SearchAsync("a", 10, CancellationToken.None);

        Assert.Equal(301, result[0].Description.Length);
        Assert.EndsWith("…", result[0].Description);
    }

    [Theory]
    [InlineData("""{"batchcomplete":""}""")]
    [InlineData("""{"query":{}}""")]
    [InlineData("""{"query":{"search":[]}}""")]
    [InlineData("""{"query":{"search":[{"snippet":"no title"},{"title":"  ","snippet":"blank"}]}}""")]
    public async Task SearchAsync_WithoutUsableHits_ReturnsNothing(string json)
    {
        var (provider, _) = Create(json);

        Assert.Empty(await provider.SearchAsync("a", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_WhenTheSiteFails_Throws()
    {
        var (provider, _) = Create("{}", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("a", 10, CancellationToken.None));
    }

    [Fact]
    public void TheKeyAndNameComeFromTheWiki()
    {
        var (provider, _) = Create("{}");

        Assert.Equal("wikipedia-bg", provider.Key);
        Assert.Equal("Уикипедия (български)", provider.DisplayName);
    }

    [Fact]
    public void TheFixedWikis_AreHttpsHostsOfWikimediaWithUniqueKeys()
    {
        Assert.All(WikimediaSourceProvider.Wikis, w => Assert.Matches(@"^[a-z]+\.(wikipedia|wikibooks|wikisource)\.org$", w.Host));
        Assert.Equal(WikimediaSourceProvider.Wikis.Count, WikimediaSourceProvider.Wikis.Select(w => w.Key).Distinct().Count());
    }
}
