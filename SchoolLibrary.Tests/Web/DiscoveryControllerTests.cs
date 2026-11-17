using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class DiscoveryControllerTests
{
    private readonly Mock<IResourceDiscoveryService> _discovery = new();
    private readonly Mock<ICategoryService> _categories = new();
    private readonly Mock<IGradeLevelService> _gradeLevels = new();

    public DiscoveryControllerTests()
    {
        _discovery.Setup(d => d.GetSources()).Returns(new[] { new DiscoverySourceInfo("a", "А"), new DiscoverySourceInfo("b", "Б") });
        _categories.Setup(c => c.GetOptionsAsync()).ReturnsAsync(new List<Category> { new() { Id = 1, Name = "Физика" } });
        _gradeLevels.Setup(g => g.GetAllAsync()).ReturnsAsync(new List<GradeLevel> { new() { Id = 1, Number = 5 } });
    }

    private DiscoveryController Controller() =>
        new DiscoveryController(_discovery.Object, _categories.Object, _gradeLevels.Object).WithUser("teacher-1", Roles.Teacher);

    private static ServiceResult<DiscoveryResult> Found(params ResourceCandidate[] candidates) =>
        ServiceResult<DiscoveryResult>.Success(new DiscoveryResult(candidates, Array.Empty<string>()));

    private static ResourceCandidate Candidate(string url = "https://a.example/1", int? existing = null) =>
        new("Заглавие", url, "Автор", "Източник", "описание", "CC BY-SA 4.0", true, existing);

    [Fact]
    public void OnlyTeachersAndAdministratorsMayUseThePage()
    {
        var authorize = typeof(DiscoveryController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.Equal(Roles.TeacherOrAdministrator, authorize.Roles);
    }

    [Fact]
    public async Task Index_FirstVisit_ShowsOnlyTheForm_WithEverySourceTicked_AndSearchesNothing()
    {
        var result = await Controller().Index(null, null, CancellationToken.None);

        var vm = Assert.IsType<DiscoveryIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(vm.Searched);
        Assert.Equal(new[] { "a", "b" }, vm.SelectedSources.OrderBy(k => k));
        Assert.Equal(2, vm.Sources.Count);
        _discovery.Verify(d => d.SearchAsync(It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_WithATopic_SearchesTheChosenKnownSources_AndShowsTheCandidatesWithTheChoices()
    {
        _discovery.Setup(d => d.SearchAsync("дроби", It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DiscoveryResult>.Success(new DiscoveryResult(new[] { Candidate(existing: 9) }, new[] { "Б" })));

        var result = await Controller().Index("дроби", new[] { "b", "unknown", "b" }, CancellationToken.None);

        var vm = Assert.IsType<DiscoveryIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.Searched);
        Assert.Equal(new[] { "b" }, vm.SelectedSources);
        var row = Assert.Single(vm.Candidates);
        Assert.Equal((9, true, "Заглавие"), (row.ExistingResourceId, row.IsEmbeddable, row.Title));
        Assert.Equal(new[] { "Б" }, vm.FailedSources);
        Assert.Single(vm.AllCategories);
        Assert.Single(vm.AllGradeLevels);
        _discovery.Verify(d => d.SearchAsync("дроби", It.Is<IEnumerable<string>?>(s => s!.SequenceEqual(new[] { "b" })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_ASearchThatIsRejected_ShowsTheMessageOnTheTopic()
    {
        _discovery.Setup(d => d.SearchAsync("а", It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, "Въведете тема от поне 2 символа."));
        var controller = Controller();

        var result = await controller.Index("а", null, CancellationToken.None);

        var vm = Assert.IsType<DiscoveryIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(vm.Searched);
        Assert.Contains(controller.ModelState["Topic"]!.Errors, e => e.ErrorMessage.Contains("поне 2"));
    }

    private static DiscoveryAddViewModel AddForm(params bool[] selected) => new()
    {
        Topic = "дроби",
        Sources = new List<string> { "a" },
        SelectedCategoryIds = new List<int> { 1 },
        SelectedGradeLevelIds = new List<int> { 5 },
        Candidates = selected.Select((s, i) => new CandidateRow
        {
            IsSelected = s, Title = "T" + i, Url = $"https://a.example/{i}", Author = "A", SourceName = "S", Description = "D", LicenseName = "CC BY-SA 4.0"
        }).ToList()
    };

    [Fact]
    public async Task Add_SendsOnlyTheTickedRows_WithTheTeacherAsOwner_AndGoesBackToTheSameSearch()
    {
        IReadOnlyList<CandidateSelection>? sent = null;
        _discovery.Setup(d => d.AddAsync(It.IsAny<IReadOnlyList<CandidateSelection>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<IReadOnlyList<int>?>(), "teacher-1"))
            .Callback<IReadOnlyList<CandidateSelection>?, IReadOnlyList<int>?, IReadOnlyList<int>?, string>((s, _, _, _) => sent = s)
            .ReturnsAsync(ServiceResult<DiscoveryAddResult>.Success(new DiscoveryAddResult(new[] { "T1" }, Array.Empty<string>())));
        var controller = Controller();

        var result = await controller.Add(AddForm(false, true, false));

        Assert.Equal(new[] { "T1" }, sent!.Select(s => s.Title));
        Assert.Equal("https://a.example/1", sent[0].Url);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("дроби", redirect.RouteValues!["topic"]);
        Assert.Equal(new List<string> { "a" }, redirect.RouteValues["sources"]);
        Assert.Equal("Добавени ресурси: 1.", controller.TempDataText("SuccessMessage"));
        Assert.Null(controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task Add_SomeSkipped_ListsThemInTheErrorMessage_AndStillReportsWhatWasAdded()
    {
        _discovery.Setup(d => d.AddAsync(It.IsAny<IReadOnlyList<CandidateSelection>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<string>()))
            .ReturnsAsync(ServiceResult<DiscoveryAddResult>.Success(new DiscoveryAddResult(new[] { "A", "B" }, new[] { "C: дубликат.", "D: грешка." })));
        var controller = Controller();

        await controller.Add(AddForm(true, true, true, true));

        Assert.Equal("Добавени ресурси: 2.", controller.TempDataText("SuccessMessage"));
        Assert.Equal("Не са добавени: C: дубликат. D: грешка.", controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task Add_NothingAdded_ShowsNoSuccessMessage()
    {
        _discovery.Setup(d => d.AddAsync(It.IsAny<IReadOnlyList<CandidateSelection>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<string>()))
            .ReturnsAsync(ServiceResult<DiscoveryAddResult>.Success(new DiscoveryAddResult(Array.Empty<string>(), new[] { "C: дубликат." })));
        var controller = Controller();

        await controller.Add(AddForm(true));

        Assert.Null(controller.TempDataText("SuccessMessage"));
        Assert.NotNull(controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task Add_WhenTheServiceRefuses_ShowsItsMessage_AndGoesBackToTheSearch()
    {
        _discovery.Setup(d => d.AddAsync(It.IsAny<IReadOnlyList<CandidateSelection>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<IReadOnlyList<int>?>(), It.IsAny<string>()))
            .ReturnsAsync(ServiceResult<DiscoveryAddResult>.Failure(ServiceError.Invalid, "Изберете поне един предмет."));
        var controller = Controller();

        var result = await controller.Add(AddForm(true));

        Assert.Equal("Изберете поне един предмет.", controller.TempDataText("ErrorMessage"));
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public void CandidateRow_CarriesAllTheFieldsBothWays()
    {
        var row = CandidateRow.From(Candidate(existing: 4));

        Assert.Equal(("Заглавие", "https://a.example/1", "Автор", "Източник", "описание", "CC BY-SA 4.0", true, 4),
            (row.Title, row.Url, row.Author, row.SourceName, row.Description, row.LicenseName, row.IsEmbeddable, row.ExistingResourceId));
        Assert.Equal(new CandidateSelection("Заглавие", "https://a.example/1", "Автор", "Източник", "описание", "CC BY-SA 4.0"), row.ToSelection());
    }
}
