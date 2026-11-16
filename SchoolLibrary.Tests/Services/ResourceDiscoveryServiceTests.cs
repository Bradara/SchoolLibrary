using Moq;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceDiscoveryServiceTests
{
    private readonly Mock<IResourceService> _resources = new();

    private static ResourceCandidate Candidate(string url, string title = "T") =>
        new(title, url, "Автор", "Източник", "описание", "CC BY-SA 4.0");

    private static Mock<IResourceSourceProvider> Provider(string key, params ResourceCandidate[] found)
    {
        var provider = new Mock<IResourceSourceProvider>();
        provider.SetupGet(p => p.Key).Returns(key);
        provider.SetupGet(p => p.DisplayName).Returns("Име " + key);
        provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(found);
        return provider;
    }

    private ResourceDiscoveryService Create(params Mock<IResourceSourceProvider>[] providers) =>
        new(providers.Select(p => p.Object), _resources.Object);

    [Fact]
    public void GetSources_ListsEveryProvider()
    {
        var service = Create(Provider("a"), Provider("b"));

        Assert.Equal(new[] { "a", "b" }, service.GetSources().Select(s => s.Key));
        Assert.Equal("Име a", service.GetSources()[0].DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("а")]
    public async Task SearchAsync_ATopicThatIsMissingOrTooShort_IsInvalid(string? topic)
    {
        var provider = Provider("a");

        var result = await Create(provider).SearchAsync(topic, null);

        Assert.Equal(ServiceError.Invalid, result.Error);
        provider.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_ATopicThatIsTooLong_IsInvalid()
    {
        var result = await Create(Provider("a")).SearchAsync(new string('а', ResourceDiscoveryService.MaxTopicLength + 1), null);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("по-дълга", result.Message);
    }

    [Fact]
    public async Task SearchAsync_SendsTheTrimmedTopicAndTheLimitToTheSource()
    {
        var provider = Provider("a");

        await Create(provider).SearchAsync("  фотосинтеза  ", null);

        provider.Verify(p => p.SearchAsync("фотосинтеза", ResourceDiscoveryService.ResultsPerSource, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithoutAChoice_UsesEverySource()
    {
        var a = Provider("a", Candidate("https://a.example/1"));
        var b = Provider("b", Candidate("https://b.example/1"));

        var result = await Create(a, b).SearchAsync("тема", null);

        Assert.Equal(2, result.Value!.Candidates.Count);
    }

    [Fact]
    public async Task SearchAsync_UsesOnlyTheChosenSources_AndIgnoresUnknownKeys()
    {
        var a = Provider("a", Candidate("https://a.example/1"));
        var b = Provider("b", Candidate("https://b.example/1"));

        var result = await Create(a, b).SearchAsync("тема", new[] { "b", "nope" });

        Assert.Equal(new[] { "https://b.example/1" }, result.Value!.Candidates.Select(c => c.Url));
        a.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_OnlyUnknownKeys_AsksToChooseASource()
    {
        var result = await Create(Provider("a")).SearchAsync("тема", new[] { "nope" });

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("източник", result.Message);
    }

    [Fact]
    public async Task SearchAsync_ASourceThatFails_IsReported_AndTheRestIsStillShown()
    {
        var good = Provider("a", Candidate("https://a.example/1"));
        var broken = Provider("b");
        broken.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var result = await Create(good, broken).SearchAsync("тема", null);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Candidates);
        Assert.Equal(new[] { "Име b" }, result.Value.FailedSources);
    }

    [Fact]
    public async Task SearchAsync_ASourceThatIsTooSlow_IsReportedAsFailed_AndTheRestIsStillShown()
    {
        var slow = Provider("a");
        slow.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, CancellationToken>(async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Array.Empty<ResourceCandidate>();
            });
        var fast = Provider("b", Candidate("https://b.example/1"));
        var service = new ResourceDiscoveryService(new[] { slow.Object, fast.Object }, _resources.Object, TimeSpan.FromMilliseconds(50));

        var result = await service.SearchAsync("тема", null);

        Assert.Equal(new[] { "Име a" }, result.Value!.FailedSources);
        Assert.Single(result.Value.Candidates);
    }

    [Fact]
    public async Task SearchAsync_TheCallerCancelling_StopsTheSearch()
    {
        var provider = Provider("a");
        provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Create(provider).SearchAsync("тема", null, cancel.Token));
    }

    [Fact]
    public async Task SearchAsync_TheSameLinkInTwoSpellingsOrSources_IsShownOnce()
    {
        var a = Provider("a", Candidate("https://example.com/page"));
        var b = Provider("b", Candidate("https://EXAMPLE.com/page/"), Candidate("https://example.com/other"));

        var result = await Create(a, b).SearchAsync("тема", null);

        Assert.Equal(2, result.Value!.Candidates.Count);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a link")]
    public async Task SearchAsync_DropsCandidatesThatAreNotHttpLinks(string url)
    {
        var result = await Create(Provider("a", Candidate(url), Candidate("https://ok.example/1"))).SearchAsync("тема", null);

        Assert.Equal(new[] { "https://ok.example/1" }, result.Value!.Candidates.Select(c => c.Url));
    }

    [Fact]
    public async Task SearchAsync_MarksTheCandidatesTheCatalogAlreadyHas()
    {
        _resources.Setup(r => r.FindByUrlAsync("https://a.example/known")).ReturnsAsync(new Resource { Id = 42 });
        var provider = Provider("a", Candidate("https://a.example/known"), Candidate("https://a.example/new"));

        var result = await Create(provider).SearchAsync("тема", null);

        Assert.Equal(new int?[] { 42, null }, result.Value!.Candidates.Select(c => c.ExistingResourceId));
    }

    [Fact]
    public async Task SearchAsync_MarksWhichCandidatesCanBeShownInsideThePlatform()
    {
        var provider = Provider("a",
            Candidate("https://bg.wikisource.org/wiki/Page"),
            Candidate("https://unknown.example/page"));

        var result = await Create(provider).SearchAsync("тема", null);

        Assert.Equal(new[] { true, false }, result.Value!.Candidates.Select(c => c.IsEmbeddable));
    }

    [Fact]
    public async Task SearchAsync_WhenNothingIsFound_ReturnsAnEmptyList()
    {
        var result = await Create(Provider("a")).SearchAsync("тема", null);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!.Candidates);
        Assert.Empty(result.Value.FailedSources);
    }
}
