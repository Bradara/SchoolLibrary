using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class YouTubeSourceProviderTests
{
    private const string ApiKey = "test-key";

    // A Russian and two Bulgarian titles, a duplicate, a bad id, a live stream, an empty title and a video that
    // the second call says is private
    private const string SearchAnswer = """
        {"items":[
          {"id":{"videoId":"ruVideo0001"},"snippet":{"title":"Обыкновенные дроби","channelTitle":"Школа","description":"Урок","liveBroadcastContent":"none"}},
          {"id":{"videoId":"bgVideo0001"},"snippet":{"title":"Обикновени дроби &amp; <b>сравняване</b>","channelTitle":"Уча се","description":"Видео урок за 5. клас","liveBroadcastContent":"none"}},
          {"id":{"videoId":"bgVideo0002"},"snippet":{"title":"Дроби","channelTitle":"","description":"","liveBroadcastContent":"none"}},
          {"id":{"videoId":"bgVideo0001"},"snippet":{"title":"Обикновени дроби","channelTitle":"Уча се","liveBroadcastContent":"none"}},
          {"id":{"videoId":"bad/../id"},"snippet":{"title":"Дроби","channelTitle":"x","liveBroadcastContent":"none"}},
          {"id":{"videoId":"liveVideo01"},"snippet":{"title":"Дроби на живо","channelTitle":"x","liveBroadcastContent":"live"}},
          {"id":{"videoId":"noTitle0001"},"snippet":{"title":"  ","channelTitle":"x","liveBroadcastContent":"none"}},
          {"id":{"videoId":"private0001"},"snippet":{"title":"Дроби частно","channelTitle":"x","liveBroadcastContent":"none"}},
          {"id":{"kind":"youtube#channel"},"snippet":{"title":"Канал"}}
        ]}
        """;

    private const string VideosAnswer = """
        {"items":[
          {"id":"ruVideo0001","status":{"privacyStatus":"public","embeddable":true,"license":"youtube"}},
          {"id":"bgVideo0001","status":{"privacyStatus":"public","embeddable":true,"license":"creativeCommon"}},
          {"id":"bgVideo0002","status":{"privacyStatus":"public","embeddable":true,"license":"youtube"}},
          {"id":"private0001","status":{"privacyStatus":"private","embeddable":true,"license":"youtube"}}
        ]}
        """;

    /// <summary>Answers the search and the videos call with their own bodies and remembers every request.</summary>
    private sealed class YouTubeHandler : HttpMessageHandler
    {
        private readonly string _search;
        private readonly string _videos;
        private readonly HttpStatusCode _status;

        public YouTubeHandler(string search, string videos, HttpStatusCode status)
        {
            _search = search;
            _videos = videos;
            _status = status;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = request.RequestUri!.AbsolutePath.EndsWith("/videos") ? _videos : _search;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (YouTubeSourceProvider Provider, YouTubeHandler Handler) Create(
        string search = SearchAnswer, string videos = VideosAnswer, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new YouTubeHandler(search, videos, status);
        return (new YouTubeSourceProvider(new FakeSourceFactory(handler), ApiKey), handler);
    }

    [Fact]
    public async Task SearchAsync_PutsBulgarianTitlesFirst_AndLeavesOutBadLiveUntitledPrivateAndDuplicateVideos()
    {
        var (provider, _) = Create();

        var result = await provider.SearchAsync("обикновени дроби", 10, CancellationToken.None);

        Assert.Equal(
            new[] { "https://www.youtube.com/watch?v=bgVideo0001", "https://www.youtube.com/watch?v=bgVideo0002", "https://www.youtube.com/watch?v=ruVideo0001" },
            result.Select(c => c.Url));
    }

    [Fact]
    public async Task SearchAsync_FillsTheCandidate_WithTheCreativeCommonsLicense()
    {
        var (provider, _) = Create();

        var video = (await provider.SearchAsync("дроби", 10, CancellationToken.None)).Single(c => c.Url.EndsWith("bgVideo0001"));

        Assert.Equal("Обикновени дроби & сравняване", video.Title);
        Assert.Equal("Уча се", video.Author);
        Assert.Equal("Видео урок за 5. клас", video.Description);
        Assert.Equal("CC BY 3.0", video.LicenseName);
        Assert.Equal("YouTube (видео уроци)", video.SourceName);
    }

    [Fact]
    public async Task SearchAsync_UsesTheStandardLicense_AndDefaults()
    {
        var (provider, _) = Create();

        var video = (await provider.SearchAsync("дроби", 10, CancellationToken.None)).Single(c => c.Url.EndsWith("bgVideo0002"));

        Assert.Equal("Стандартен лиценз на YouTube", video.LicenseName);
        Assert.Equal("YouTube", video.Author);
        Assert.Equal("Видео в YouTube.", video.Description);
    }

    [Fact]
    public async Task SearchAsync_SendsTheKeyInAHeader_AndOnlyTheTopicWords()
    {
        var (provider, handler) = Create();

        await provider.SearchAsync("дроби & числов лъч", 10, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var search = handler.Requests[0];
        Assert.Equal("www.googleapis.com", search.RequestUri!.Host);
        Assert.Equal("/youtube/v3/search", search.RequestUri.AbsolutePath);
        Assert.Contains("videoEmbeddable=true", search.RequestUri.Query);
        Assert.Contains("safeSearch=strict", search.RequestUri.Query);
        Assert.EndsWith("&q=" + Uri.EscapeDataString("дроби числов лъч"), search.RequestUri.Query);
        Assert.Equal(ApiKey, search.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.All(handler.Requests, r => Assert.DoesNotContain(ApiKey, r.RequestUri!.ToString()));

        var videos = handler.Requests[1];
        Assert.Equal("/youtube/v3/videos", videos.RequestUri!.AbsolutePath);
        Assert.Contains("part=status", videos.RequestUri.Query);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostTheAskedNumber()
    {
        var (provider, _) = Create();

        Assert.Single(await provider.SearchAsync("дроби", 1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_OnlyStopWords_SendsNoRequest()
    {
        var (provider, handler) = Create();

        Assert.Empty(await provider.SearchAsync("и на или", 10, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_NoVideosFound_SkipsTheLicenseCall()
    {
        var (provider, handler) = Create(search: """{"items":[]}""");

        Assert.Empty(await provider.SearchAsync("дроби", 10, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_UnexpectedAnswer_ReturnsNothing()
    {
        var (provider, _) = Create(search: """{"error":"x"}""");

        Assert.Empty(await provider.SearchAsync("дроби", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ErrorStatus_Throws()
    {
        var (provider, _) = Create(status: HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync("дроби", 10, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithoutAKey_Throws(string key)
    {
        Assert.Throws<ArgumentException>(() => new YouTubeSourceProvider(new FakeSourceFactory(new YouTubeHandler("", "", HttpStatusCode.OK)), key));
    }

    [Fact]
    public void Registration_AddsYouTube_OnlyWithAKey()
    {
        static IEnumerable<string> Keys(string? apiKey)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplicationServices(apiKey);
            using var provider = services.BuildServiceProvider();
            return provider.GetServices<IResourceSourceProvider>().Select(p => p.Key).ToList();
        }

        Assert.DoesNotContain("youtube", Keys(null));
        Assert.DoesNotContain("youtube", Keys(" "));
        Assert.Contains("youtube", Keys("key"));
    }
}
