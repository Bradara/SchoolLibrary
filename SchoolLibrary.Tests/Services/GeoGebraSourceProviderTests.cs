using System.Net;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class GeoGebraSourceProviderTests
{
    // The shape of the search answer: Russian and Bulgarian titles mixed, a copy, a bad id, a private material
    private const string Answer = """
        {"hits":[
          {"id":"VASaRbSX","title":"Обыкновенные дроби.","type":"ws","visibility":"O","creator":{"id":1,"name":"Valtntina"}},
          {"id":"rt6ajmre","title":"ОБИКНОВЕНИ ДРОБИ","type":"ws","visibility":"O","creator":{"id":2,"name":"Ирина Стойкова"}},
          {"id":"W8PDrJ7Q","title":"Обикновени дроби - визуализация","type":"book","visibility":"O","creator":{"id":3,"name":"didijordanova"}},
          {"id":"kju3dvpq","title":"Копие на Обикновени дроби","type":"ws","visibility":"O","creator":{"id":4,"name":"x"}},
          {"id":"zzz/../x","title":"Дроби","type":"ws","visibility":"O","creator":{"id":5,"name":"x"}},
          {"id":"privat01","title":"Дроби","type":"ws","visibility":"P","creator":{"id":6,"name":"x"}},
          {"id":"applet01","title":"Fractions <b>applet</b>","type":"ggb","visibility":"O"},
          {"id":"notitle1","title":"  ","type":"ws","visibility":"O"}
        ]}
        """;

    private static (GeoGebraSourceProvider Provider, FakeSourceHandler Handler) Create(string body = Answer, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeSourceHandler(body, status);
        return (new GeoGebraSourceProvider(new FakeSourceFactory(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_PutsBulgarianTitlesFirst_AndLeavesOutCopiesBadIdsPrivateAndUntitled()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("обикновени дроби", 10, CancellationToken.None);

        Assert.Equal(new[] { "rt6ajmre", "W8PDrJ7Q", "VASaRbSX", "applet01" }, result.Select(c => c.Url.Split('/').Last()));
    }

    [Fact]
    public async Task SearchAsync_FillsTheCandidate()
    {
        var (provider, _) = Create();

        var book = (await provider.SearchAsync("дроби", 10, CancellationToken.None)).Single(c => c.Url.EndsWith("W8PDrJ7Q"));

        Assert.Equal("Обикновени дроби - визуализация", book.Title);
        Assert.Equal("https://www.geogebra.org/m/W8PDrJ7Q", book.Url);
        Assert.Equal("didijordanova", book.Author);
        Assert.Equal("Лиценз на GeoGebra", book.LicenseName);
        Assert.Equal("Сборник с дейности в GeoGebra.", book.Description);
        Assert.Equal("GeoGebra (интерактивни материали)", book.SourceName);
    }

    [Fact]
    public async Task SearchAsync_CleansTheTitle_AndUsesADefaultAuthor()
    {
        var (provider, _) = Create();

        var applet = (await provider.SearchAsync("дроби", 10, CancellationToken.None)).Single(c => c.Url.EndsWith("applet01"));

        Assert.Equal("Fractions applet", applet.Title);
        Assert.Equal("GeoGebra", applet.Author);
        Assert.Equal("Аплет в GeoGebra.", applet.Description);
    }

    [Fact]
    public async Task SearchAsync_SendsOnlyTheTopicWords_Escaped()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("дроби & числов лъч", 10, CancellationToken.None);

        Assert.Equal("api.geogebra.org", handler.LastUri!.Host);
        Assert.Equal("?query=" + Uri.EscapeDataString("дроби числов лъч"), handler.LastUri.Query);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostTheAskedNumber()
    {
        var (provider, _) = Create();

        Assert.Equal(2, (await provider.SearchAsync("дроби", 2, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task SearchAsync_OnlyStopWords_SendsNoRequest()
    {
        var (provider, handler) = Create();

        Assert.Empty(await provider.SearchAsync("и на или", 10, CancellationToken.None));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"hits\":{}}")]
    [InlineData("{\"error\":{\"code\":\"err_unsupported_arguments\"}}")]
    public async Task SearchAsync_AnUnexpectedAnswer_GivesNoCandidates(string body)
    {
        var (provider, _) = Create(body);

        Assert.Empty(await provider.SearchAsync("дроби", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_AnErrorStatus_Throws_SoTheSourceIsReportedAsFailed()
    {
        var (provider, _) = Create("{}", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("дроби", 10, CancellationToken.None));
    }
}
