using System.Net;
using System.Text;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class PhetSourceProviderTests
{
    private const string Catalog = """
        {"projects":[
          {"name":"html/fractions-intro","simulations":[
            {"name":"fractions-intro","localizedSimulations":{"bg":{"title":"Дроби: Въведение","runUrl":"/sims/html/fractions-intro/latest/fractions-intro_bg.html","learningGoals":"Разглеждане на дроби като част от цяло."}}},
            {"name":"build-a-fraction","localizedSimulations":{"bg":{"title":"Част от цяло","runUrl":"/sims/html/build-a-fraction/latest/build-a-fraction_bg.html","learningGoals":""}}}]},
          {"name":"html/gravity-and-orbits","simulations":[
            {"name":"gravity-and-orbits","localizedSimulations":{"bg":{"title":"Гравитация и орбити","runUrl":"/sims/html/gravity-and-orbits/latest/gravity-and-orbits_bg.html","learningGoals":"Дроби не са тук."}}},
            {"name":"only-english","localizedSimulations":{"en":{"title":"English only","runUrl":"/sims/html/x/latest/x_en.html"}}},
            {"name":"evil","localizedSimulations":{"bg":{"title":"Злонамерена симулация","runUrl":"https://evil.example/x.html"}}},
            {"name":"no-title","localizedSimulations":{"bg":{"title":" ","runUrl":"/sims/html/y/latest/y_bg.html"}}}]}]}
        """;

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }

        public CountingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
        }
    }

    private static (PhetSourceProvider Provider, CountingHandler Handler) Create(string json = Catalog, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CountingHandler(json, status);
        return (new PhetSourceProvider(new FakeFactory(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_FindsSimulationsByTheBulgarianTitle()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("гравитация", 10, CancellationToken.None);

        var found = Assert.Single(result);
        Assert.Equal("Симулация: Гравитация и орбити", found.Title);
        Assert.Equal("https://phet.colorado.edu/sims/html/gravity-and-orbits/latest/gravity-and-orbits_bg.html", found.Url);
        Assert.Equal("CC BY-NC 4.0", found.LicenseName);
        Assert.Contains("PhET", found.Author);
        Assert.Equal("Дроби не са тук.", found.Description);
    }

    [Fact]
    public async Task SearchAsync_AlsoMatchesTheLearningGoalsAndTheEnglishName_IgnoringCase()
    {
        var (provider, _) = Create();

        var byGoals = await provider.SearchAsync("ЧАСТ ОТ ЦЯЛО", 10, CancellationToken.None);
        var byName = await provider.SearchAsync("Build A Fraction", 10, CancellationToken.None);

        Assert.Equal(2, byGoals.Count);
        Assert.Equal("Симулация: Част от цяло", Assert.Single(byName).Title);
    }

    [Fact]
    public async Task SearchAsync_EveryWordMustMatch_AndTitleMatchesComeFirst()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("дроби", 10, CancellationToken.None);

        // "Дроби: Въведение" has the word in its title, "Гравитация и орбити" only in its goals
        Assert.Equal(new[] { "Симулация: Дроби: Въведение", "Симулация: Гравитация и орбити" }, result.Select(c => c.Title));
        Assert.Empty(await provider.SearchAsync("дроби орбити въведение", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_HonoursTheLimit()
    {
        var (provider, _) = Create();

        Assert.Single(await provider.SearchAsync("дроби", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_SkipsSimulationsWithoutBulgarianTitleOrWithAnAddressOutsidePhet()
    {
        var (provider, _) = Create();

        Assert.Empty(await provider.SearchAsync("English", 10, CancellationToken.None));
        Assert.Empty(await provider.SearchAsync("Злонамерена", 10, CancellationToken.None));
        Assert.Empty(await provider.SearchAsync("no-title", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ASimulationWithoutLearningGoals_GetsAGenericDescription()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("част от цяло", 10, CancellationToken.None);

        Assert.Equal("Интерактивна симулация на PhET.", result.Single(c => c.Title.EndsWith("Част от цяло")).Description);
    }

    [Fact]
    public async Task SearchAsync_DownloadsTheCatalogOnlyOnce()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("дроби", 10, CancellationToken.None);
        await provider.SearchAsync("гравитация", 10, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal("phet.colorado.edu", handler.LastUri!.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_WithoutWords_ReturnsNothingAndDownloadsNothing(string query)
    {
        var (provider, handler) = Create();

        Assert.Empty(await provider.SearchAsync(query, 10, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SearchAsync_WhenPhetFails_Throws_AndTriesAgainNextTime()
    {
        var (provider, handler) = Create("{}", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("дроби", 10, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("дроби", 10, CancellationToken.None));

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task SearchAsync_ACatalogWithoutProjects_FindsNothing()
    {
        var (provider, _) = Create("{}");

        Assert.Empty(await provider.SearchAsync("дроби", 10, CancellationToken.None));
    }

    [Fact]
    public void TheKeyAndNameAreSet()
    {
        var (provider, _) = Create();

        Assert.Equal("phet", provider.Key);
        Assert.Contains("PhET", provider.DisplayName);
    }
}
