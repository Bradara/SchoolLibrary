using Moq;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceDiscoveryServiceTests
{
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<ILicenseService> _licenses = new();

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
        new(providers.Select(p => p.Object), _resources.Object, _licenses.Object);

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
        var service = new ResourceDiscoveryService(new[] { slow.Object, fast.Object }, _resources.Object, _licenses.Object, TimeSpan.FromMilliseconds(50));

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

    private static CandidateSelection Selection(string url = "https://a.example/1", string title = "Заглавие", string license = "CC BY-SA 4.0") =>
        new(title, url, "Автор", "Уикипедия", "описание", license);

    private void WithLicenses() =>
        _licenses.Setup(l => l.GetOptionsAsync()).ReturnsAsync(new List<License> { new() { Id = 7, Name = "CC BY-SA 4.0" }, new() { Id = 8, Name = "CC0 1.0" } });

    private void CreateSucceeds() =>
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), It.IsAny<string?>()))
            .ReturnsAsync((ResourceInput i, string? _) => ServiceResult<Resource>.Success(new Resource { Title = i.Title.Trim() }));

    [Fact]
    public async Task AddAsync_SavesEverySuggestionAsAFreeResourceOfTheTeacher_WithTheChosenSubjectsAndGrades()
    {
        WithLicenses();
        ResourceInput? saved = null;
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), "teacher-1"))
            .Callback<ResourceInput, string?>((i, _) => saved = i)
            .ReturnsAsync((ResourceInput i, string? _) => ServiceResult<Resource>.Success(new Resource { Title = i.Title }));

        var result = await Create().AddAsync(new[] { Selection() }, new[] { 1, 2 }, new[] { 5 }, "teacher-1");

        Assert.Equal(new[] { "Заглавие" }, result.Value!.Added);
        Assert.Empty(result.Value.Skipped);
        Assert.Equal(Data.Enums.ResourceKind.External, saved!.Kind);
        Assert.Equal(7, saved.LicenseId);
        Assert.Equal(new[] { 1, 2 }, saved.CategoryIds);
        Assert.Equal(new[] { 5 }, saved.GradeLevelIds);
        Assert.Equal(("https://a.example/1", "Автор", "Уикипедия", "описание"), (saved.Url, saved.Author, saved.Source, saved.Description));
    }

    [Fact]
    public async Task AddAsync_TheLicenseNameIsMatchedIgnoringCase()
    {
        WithLicenses();
        CreateSucceeds();

        var result = await Create().AddAsync(new[] { Selection(license: " cc0 1.0 ") }, new[] { 1 }, new[] { 5 }, "t");

        Assert.Single(result.Value!.Added);
        _resources.Verify(r => r.CreateAsync(It.Is<ResourceInput>(i => i.LicenseId == 8), "t"), Times.Once);
    }

    [Fact]
    public async Task AddAsync_ASuggestionThatFails_IsSkippedWithTheReason_AndTheRestIsSaved()
    {
        WithLicenses();
        _resources.Setup(r => r.CreateAsync(It.Is<ResourceInput>(i => i.Url == "https://a.example/dup"), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<Resource>.Failure(ServiceError.Conflict, "Този линк вече е в библиотеката."));
        _resources.Setup(r => r.CreateAsync(It.Is<ResourceInput>(i => i.Url == "https://a.example/new"), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<Resource>.Success(new Resource { Title = "Нов" }));

        var result = await Create().AddAsync(
            new[] { Selection("https://a.example/dup", "Дубликат"), Selection("https://a.example/new", "Нов") },
            new[] { 1 }, new[] { 5 }, "t");

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "Нов" }, result.Value!.Added);
        Assert.Equal(new[] { "Дубликат: Този линк вече е в библиотеката." }, result.Value.Skipped);
    }

    [Theory]
    [InlineData("Непознат лиценз")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AddAsync_AnUnknownLicense_IsSkippedWithoutSaving(string? license)
    {
        WithLicenses();

        var result = await Create().AddAsync(new[] { Selection(license: license!) }, new[] { 1 }, new[] { 5 }, "t");

        Assert.Empty(result.Value!.Added);
        Assert.Contains("лицензът не е намерен", Assert.Single(result.Value.Skipped));
        _resources.Verify(r => r.CreateAsync(It.IsAny<ResourceInput>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_WithNothingTicked_IsInvalid()
    {
        var none = await Create().AddAsync(Array.Empty<CandidateSelection>(), new[] { 1 }, new[] { 5 }, "t");
        var missing = await Create().AddAsync(null, new[] { 1 }, new[] { 5 }, "t");

        Assert.Equal(ServiceError.Invalid, none.Error);
        Assert.Equal(ServiceError.Invalid, missing.Error);
    }

    [Fact]
    public async Task AddAsync_WithoutASubject_OrAGrade_IsInvalid_AndSavesNothing()
    {
        var noSubject = await Create().AddAsync(new[] { Selection() }, Array.Empty<int>(), new[] { 5 }, "t");
        var noGrade = await Create().AddAsync(new[] { Selection() }, new[] { 1 }, null, "t");

        Assert.Contains("предмет", noSubject.Message);
        Assert.Contains("клас", noGrade.Message);
        _resources.Verify(r => r.CreateAsync(It.IsAny<ResourceInput>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AddAsync_TooManyAtOnce_IsInvalid()
    {
        var many = Enumerable.Range(0, ResourceDiscoveryService.MaxAddAtOnce + 1).Select(i => Selection($"https://a.example/{i}")).ToList();

        var result = await Create().AddAsync(many, new[] { 1 }, new[] { 5 }, "t");

        Assert.Equal(ServiceError.Invalid, result.Error);
        _resources.Verify(r => r.CreateAsync(It.IsAny<ResourceInput>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("и на")]
    [InlineData("и")]
    [InlineData("или")]
    [InlineData("?!")]
    public async Task SearchAsync_ATopicOfOnlyJoiningWords_IsInvalid_AndAsksNoSource(string topic)
    {
        var provider = Provider("a");

        var result = await Create(provider).SearchAsync(topic, null);

        Assert.Equal(ServiceError.Invalid, result.Error);
        provider.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_SendsTheSourcesOnlyTheSignificantWords_AndReportsThem()
    {
        var provider = Provider("a", Candidate("https://a.example/1"));

        var result = await Create(provider).SearchAsync("  Религия и култура на Древен Египет!  ", null);

        provider.Verify(p => p.SearchAsync("религия култура древен египет", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "религия", "култура", "древен", "египет" }, result.Value!.Terms);
    }
}
