using System.Net;
using System.Text;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class InternetArchiveSourceProviderTests
{
    private const string Answer = """
        {"response":{"numFound":6,"start":0,"docs":[
          {"identifier":"perelman_physics","title":"Занимательная физика","creator":"Перельман Я.И.","description":"<p>Книга &amp; задачи\n  по физика</p>","licenseurl":"https://creativecommons.org/publicdomain/mark/1.0/"},
          {"identifier":"physics_video","title":"Physics lesson","creator":["A. Teacher","B. Teacher"],"licenseurl":"http://creativecommons.org/licenses/by-sa/4.0/"},
          {"identifier":"no_creator","title":"Без автор","licenseurl":"https://creativecommons.org/licenses/by/4.0/"},
          {"identifier":"nd_license","title":"No derivatives","creator":"X","licenseurl":"https://creativecommons.org/licenses/by-nc-nd/4.0/"},
          {"identifier":"bad id/../x","title":"Bad identifier","creator":"X","licenseurl":"https://creativecommons.org/licenses/by/4.0/"},
          {"identifier":"no_title","creator":"X","licenseurl":"https://creativecommons.org/licenses/by/4.0/"}]}}
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
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
        }
    }

    private static (InternetArchiveSourceProvider Provider, FakeHandler Handler) Create(string body = Answer, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(body, status);
        return (new InternetArchiveSourceProvider(new FakeFactory(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_KeepsOnlyItemsWithALicenseThePlatformKnowsAndARealIdentifierAndTitle()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("физика", 10, CancellationToken.None);

        Assert.Equal(new[] { "perelman_physics", "physics_video", "no_creator" },
            result.Select(c => c.Url.Split('/').Last()));
    }

    [Fact]
    public async Task SearchAsync_FillsTheCandidateFromTheItem()
    {
        var (provider, _) = Create();

        var book = (await provider.SearchAsync("физика", 10, CancellationToken.None))[0];

        Assert.Equal("Занимательная физика", book.Title);
        Assert.Equal("https://archive.org/details/perelman_physics", book.Url);
        Assert.Equal("Перельман Я.И.", book.Author);
        Assert.Equal("Обществено достояние", book.LicenseName);
        Assert.Equal("Книга & задачи по физика", book.Description);
        Assert.Equal("Internet Archive (книги и видео)", book.SourceName);
    }

    [Fact]
    public async Task SearchAsync_JoinsSeveralCreators_AndFallsBackWhenThereIsNone()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("физика", 10, CancellationToken.None);

        Assert.Equal("A. Teacher, B. Teacher", result[1].Author);
        Assert.Equal("CC BY-SA 4.0", result[1].LicenseName);
        Assert.Equal("Internet Archive", result[2].Author);
        Assert.Equal(string.Empty, result[2].Description);
    }

    [Fact]
    public async Task SearchAsync_HonoursTheLimit()
    {
        var (provider, _) = Create();

        Assert.Single(await provider.SearchAsync("физика", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_AsksOnlyForTitleWords_OfBooksAndVideos_WithCreativeCommonsLicenses()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("физика 7", 10, CancellationToken.None);

        var uri = handler.LastUri!;
        Assert.Equal("archive.org", uri.Host);
        var query = Uri.UnescapeDataString(uri.Query.Split("&q=")[1]);
        Assert.Equal("title:(физика AND 7) AND mediatype:(texts OR movies) AND licenseurl:*creativecommons*", query);
        Assert.Contains("rows=30", uri.Query);
        Assert.Contains("sort%5B%5D=downloads", uri.Query);
    }

    [Fact]
    public async Task SearchAsync_TheUserCannotAddSearchFieldsOrOperators()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("""x" OR licenseurl:* ) AND (creator:"y""", 10, CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastUri!.Query.Split("&q=")[1]);
        Assert.Equal("title:(x AND licenseurl AND creator AND y) AND mediatype:(texts OR movies) AND licenseurl:*creativecommons*", query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("!!! ???")]
    [InlineData("AND OR NOT")]
    public async Task SearchAsync_WithoutRealWords_AsksNothing(string topic)
    {
        var (provider, handler) = Create();

        Assert.Empty(await provider.SearchAsync(topic, 10, CancellationToken.None));
        Assert.Null(handler.LastUri);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"response":{}}""")]
    [InlineData("""{"response":{"docs":[]}}""")]
    public async Task SearchAsync_WithoutItems_ReturnsNothing(string json)
    {
        var (provider, _) = Create(json);

        Assert.Empty(await provider.SearchAsync("физика", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_WhenTheArchiveFails_Throws()
    {
        var (provider, _) = Create("{}", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("физика", 10, CancellationToken.None));
    }

    [Theory]
    [InlineData("https://creativecommons.org/publicdomain/zero/1.0/", "CC0 1.0")]
    [InlineData("http://creativecommons.org/publicdomain/mark/1.0/", "Обществено достояние")]
    [InlineData("https://creativecommons.org/licenses/by/4.0/", "CC BY 4.0")]
    [InlineData("http://creativecommons.org/licenses/by/3.0/us/", "CC BY 3.0")]
    [InlineData("https://creativecommons.org/licenses/by-sa/4.0/", "CC BY-SA 4.0")]
    [InlineData("https://creativecommons.org/licenses/by-nc/4.0/", "CC BY-NC 4.0")]
    [InlineData("https://creativecommons.org/licenses/by-nc-sa/4.0/", "CC BY-NC-SA 4.0")]
    [InlineData("HTTPS://CREATIVECOMMONS.ORG/LICENSES/BY/4.0/", "CC BY 4.0")]
    [InlineData("https://creativecommons.org/licenses/by-nd/4.0/", null)]
    [InlineData("https://creativecommons.org/licenses/by-nc-nd/4.0/", null)]
    [InlineData("https://creativecommons.org/licenses/by/2.0/", null)]
    [InlineData("https://example.com/license", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void LicenseNameFor_MapsOnlyLicensesThePlatformHas(string? url, string? expected)
    {
        Assert.Equal(expected, InternetArchiveSourceProvider.LicenseNameFor(url));
    }

    [Fact]
    public void ThePlatformHasEveryLicenseTheProviderCanOffer()
    {
        // The names must exist in the seeded license list, otherwise a suggestion could not be saved
        var seeded = SchoolLibrary.Data.Seeding.ApplicationDbSeeder.StandardLicenseNames;
        var offered = new[]
        {
            "https://creativecommons.org/publicdomain/zero/1.0/", "https://creativecommons.org/publicdomain/mark/1.0/",
            "https://creativecommons.org/licenses/by/4.0/", "https://creativecommons.org/licenses/by/3.0/",
            "https://creativecommons.org/licenses/by-sa/4.0/", "https://creativecommons.org/licenses/by-nc/4.0/",
            "https://creativecommons.org/licenses/by-nc-sa/4.0/"
        }.Select(InternetArchiveSourceProvider.LicenseNameFor);

        Assert.All(offered, name => Assert.Contains(name, seeded));
    }

    [Theory]
    [InlineData("https://archive.org/details/perelman_physics", "https://archive.org/embed/perelman_physics")]
    [InlineData("https://www.archive.org/details/A-b.c_1/", "https://archive.org/embed/A-b.c_1")]
    [InlineData("http://archive.org/details/x1", "https://archive.org/embed/x1")]
    public void ArchiveItemPages_AreEmbeddedWithThePlayerAddress(string url, string embed)
    {
        var resource = new Resource { Url = url };

        Assert.True(resource.IsEmbeddable);
        Assert.Equal(embed, resource.EmbedUrl);
    }

    [Theory]
    [InlineData("https://archive.org/")]
    [InlineData("https://archive.org/search?query=x")]
    [InlineData("https://archive.org/details/")]
    [InlineData("https://archive.org/details/x/extra")]
    [InlineData("https://archive.org/details/../etc")]
    [InlineData("https://archive.org.evil.com/details/x1")]
    [InlineData("https://evilarchive.org/details/x1")]
    public void OtherArchivePages_AndLookAlikes_AreNotEmbedded(string url)
    {
        Assert.False(new Resource { Url = url }.IsEmbeddable);
    }
}
