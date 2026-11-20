using System.Net;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class ScratchSourceProviderTests
{
    // The shape of the search answer (a JSON array of projects)
    private const string Answer = """
        [
          {"id":1001,"title":"Многоугольник","instructions":"","description":"многоъгълник на руски","author":{"username":"ru_teacher"},"history":{"shared":"2020-01-01T10:00:00.000Z"},"stats":{"loves":500,"views":9000}},
          {"id":1002,"title":"Многоъгълник","instructions":"Рисуване на <b>многоъгълник</b> с молив.","description":"бележки","author":{"username":"bg_teacher"},"history":{"shared":"2021-08-06T10:00:00.000Z"},"stats":{"loves":3,"views":399}},
          {"id":1003,"title":"Многоъгълник с подпрограма","instructions":"","description":"Собствен блок.","author":{"username":"bg_two"},"history":{"shared":"2022-01-01T10:00:00.000Z"},"stats":{"loves":1,"views":20}},
          {"id":1004,"title":"Многоъгълник 2026","instructions":"","description":"","author":{"username":"new_user"},"history":{"shared":"2026-01-22T00:00:00.000Z"},"stats":{"loves":90,"views":5000}},
          {"id":1005,"title":"Многоъгълник без дата","author":{"username":"x"},"history":{}},
          {"id":"1006","title":"Многоъгълник с id текст","author":{"username":"x"},"history":{"shared":"2020-01-01T00:00:00Z"}},
          {"id":1007,"title":"Котка","instructions":"Нищо общо.","description":"","author":{"username":"x"},"history":{"shared":"2020-01-01T00:00:00Z"}},
          {"id":1009,"title":"Многоъгълник-Иван","instructions":"","description":"","author":{"username":"child_3b"},"history":{"shared":"2022-03-01T00:00:00Z"},"stats":{"loves":2,"views":19}},
          {"id":1010,"title":"Многоъгълник без статистика","instructions":"","description":"","author":{"username":"x"},"history":{"shared":"2022-03-01T00:00:00Z"}},
          {"id":1008,"title":"   ","instructions":"многоъгълник","author":{"username":"x"},"history":{"shared":"2020-01-01T00:00:00Z"}}
        ]
        """;

    private static (ScratchSourceProvider Provider, FakeSourceHandler Handler) Create(string body = Answer, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeSourceHandler(body, status);
        return (new ScratchSourceProvider(new FakeSourceFactory(handler)), handler);
    }

    private static string IdOf(ResourceCandidate candidate) => candidate.Url.TrimEnd('/').Split('/').Last();

    [Fact]
    public async Task SearchAsync_KeepsProjectsSharedBeforeTheLicenseChange_BulgarianFirst()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("многоъгълник", 10, CancellationToken.None);

        // 1004 is shared on the day of the change, 1005 has no date, 1006 has no numeric id, 1007 has none of
        // the words, 1008 has no title, 1009 and 1010 are seen too few times. Among the Bulgarian ones the more loved is first.
        Assert.Equal(new[] { "1002", "1003", "1001" }, result.Select(IdOf));
    }

    [Fact]
    public async Task SearchAsync_MoreWordsInTheTitleComeBeforeMoreLoves()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("многоъгълник подпрограма", 10, CancellationToken.None);

        Assert.Equal(new[] { "1003", "1002" }, result.Take(2).Select(IdOf));
    }

    [Fact]
    public async Task SearchAsync_FillsTheCandidate_FromTheInstructions()
    {
        var (provider, _) = Create();

        var project = (await provider.SearchAsync("многоъгълник", 10, CancellationToken.None)).Single(c => c.Author == "bg_teacher");

        Assert.Equal("Многоъгълник", project.Title);
        Assert.Equal("https://scratch.mit.edu/projects/1002/", project.Url);
        Assert.Equal("Рисуване на многоъгълник с молив.", project.Description);
        Assert.Equal("CC BY-SA 2.0", project.LicenseName);
        Assert.Equal("Scratch (проекти с блоково програмиране)", project.SourceName);
    }

    [Fact]
    public async Task SearchAsync_WithoutInstructions_UsesTheNotes()
    {
        var (provider, _) = Create();

        var project = (await provider.SearchAsync("многоъгълник", 10, CancellationToken.None)).Single(c => c.Author == "bg_two");

        Assert.Equal("Собствен блок.", project.Description);
    }

    [Fact]
    public async Task SearchAsync_SendsOnlyTheTopicWords_Escaped()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("рисуване & молив", 10, CancellationToken.None);

        Assert.Equal("api.scratch.mit.edu", handler.LastUri!.Host);
        Assert.EndsWith("&q=" + Uri.EscapeDataString("рисуване молив"), handler.LastUri.Query);
        Assert.Contains($"limit={ScratchSourceProvider.RequestedProjects}", handler.LastUri.Query);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostTheAskedNumber()
    {
        var (provider, _) = Create();

        Assert.Single(await provider.SearchAsync("многоъгълник", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_OnlyStopWords_SendsNoRequest()
    {
        var (provider, handler) = Create();

        Assert.Empty(await provider.SearchAsync("и на", 10, CancellationToken.None));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{\"title\":\"многоъгълник без id\"}]")]
    public async Task SearchAsync_AnUnexpectedAnswer_GivesNoCandidates(string body)
    {
        var (provider, _) = Create(body);

        Assert.Empty(await provider.SearchAsync("многоъгълник", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_AnErrorStatus_Throws_SoTheSourceIsReportedAsFailed()
    {
        var (provider, _) = Create("[]", HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("многоъгълник", 10, CancellationToken.None));
    }
}
