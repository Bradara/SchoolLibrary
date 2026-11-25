using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class ResourceCardModelTests
{
    private static Resource NewResource(params Category[] subjects)
    {
        var resource = new Resource { Id = 1, Title = "Дроби", Url = "https://www.youtube.com/watch?v=x" };
        foreach (var subject in subjects)
        {
            resource.Categories.Add(subject);
        }

        return resource;
    }

    [Fact]
    public void Subjects_AreInBulgarianOrder_AndTheFirstGivesTheHeader()
    {
        var music = new Category { Name = "Музика", Icon = "bi-music-note-beamed", Color = "#9c36b5" };
        var art = new Category { Name = "Изобразително изкуство", Icon = "bi-palette", Color = "#e8590c" };

        var card = new ResourceCardModel(NewResource(music, art), null, "/");

        Assert.Equal(new[] { "Изобразително изкуство", "Музика" }, card.Subjects.Select(s => s.Name));
        Assert.Equal("#e8590c", card.Color);
        Assert.Equal("bi-palette", card.HeaderIcon);
    }

    [Fact]
    public void WithoutSubjects_TheHeaderIsGrayWithTheMediaIcon()
    {
        var card = new ResourceCardModel(NewResource(), null, "/");

        Assert.Equal(SubjectAppearance.DefaultColor, card.Color);
        Assert.Equal(card.Media.Icon, card.HeaderIcon);
    }

    [Theory]
    [InlineData("  Иван Вазов ", "Иван Вазов")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Author_IsTrimmed_AndMissingWhenEmpty(string author, string? expected)
    {
        var resource = NewResource();
        resource.Author = author;

        Assert.Equal(expected, new ResourceCardModel(resource, null, "/").Author);
    }

    [Fact]
    public void AColorOutsideTheList_IsNotPutIntoTheStyle()
    {
        var odd = new Category { Name = "Музика", Icon = "bi-x\" onclick=\"x", Color = "red;background:url(x)" };

        var card = new ResourceCardModel(NewResource(odd), null, "/");

        Assert.Equal(SubjectAppearance.DefaultColor, card.Color);
        Assert.Equal(SubjectAppearance.DefaultIcon, card.HeaderIcon);
    }

    [Fact]
    public void Grades_AreInOrder()
    {
        var resource = NewResource();
        resource.GradeLevels.Add(new GradeLevel { Number = 7 });
        resource.GradeLevels.Add(new GradeLevel { Number = 5 });

        var card = new ResourceCardModel(resource, true, "/");

        Assert.Equal(new[] { 5, 7 }, card.Grades.Select(g => g.Number));
    }

    [Fact]
    public void Media_IsRecognizedFromTheLink()
    {
        Assert.Equal(ResourceMediaType.Video, new ResourceCardModel(NewResource(), null, "/").Media.Type);
    }

    [Fact]
    public void Visibility_IsShownOnlyWhenRestricted()
    {
        var everyone = new ResourceCardModel(NewResource(), null, "/");
        var restricted = NewResource();
        restricted.Audience = ResourceAudience.Teachers;
        var teachersOnly = new ResourceCardModel(restricted, null, "/");

        Assert.False(everyone.IsRestricted);
        Assert.True(teachersOnly.IsRestricted);
        Assert.StartsWith("Видимост: ", teachersOnly.VisibilityText);
    }

    [Theory]
    [InlineData("bi-book", "#2f6fed", "bi-book", "#2f6fed")]
    [InlineData(null, null, SubjectAppearance.DefaultIcon, SubjectAppearance.DefaultColor)]
    [InlineData("bi-book\" onmouseover=\"x", "#2f6fed\" x", SubjectAppearance.DefaultIcon, SubjectAppearance.DefaultColor)]
    [InlineData("bi-book", "#123456", "bi-book", SubjectAppearance.DefaultColor)]
    public void SubjectLook_KeepsOnlyValuesFromTheLists(string? icon, string? color, string expectedIcon, string expectedColor)
    {
        Assert.Equal((expectedIcon, expectedColor), SubjectLook.Of(icon, color));
        Assert.Equal((expectedIcon, expectedColor), SubjectLook.Of(new Category { Icon = icon!, Color = color! }));
    }
}
