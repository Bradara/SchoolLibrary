using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using Xunit;

namespace SchoolLibrary.Tests.Services;

public class SectionPathsTests
{
    private static Section S(int id, string name, int? parentId = null)
        => new() { Id = id, Name = name, ParentId = parentId };

    [Fact]
    public void Build_GivesFullPathsInTreeOrder()
    {
        var sections = new[]
        {
            S(3, "9 клас", 1),
            S(2, "ИТ"),
            S(4, "9 клас", 2),
            S(1, "Информатика"),
            S(5, "Цикли", 3)
        };

        var result = SectionPaths.Build(sections);

        Assert.Equal(new[]
        {
            new SectionOption(2, "ИТ"),
            new SectionOption(4, "ИТ > 9 клас"),
            new SectionOption(1, "Информатика"),
            new SectionOption(3, "Информатика > 9 клас"),
            new SectionOption(5, "Информатика > 9 клас > Цикли")
        }, result);
    }

    [Fact]
    public void Build_KeepsTheGivenOrderOfSiblings()
    {
        var result = SectionPaths.Build(new[] { S(1, "Б"), S(2, "А"), S(3, "Я", 1), S(4, "В", 1) });

        Assert.Equal(new[] { 1, 3, 4, 2 }, result.Select(o => o.Id));
    }

    [Fact]
    public void Build_TreatsASectionWithAMissingParentAsARoot()
    {
        var result = SectionPaths.Build(new[] { S(5, "Цикли", 99) });

        Assert.Equal(new SectionOption(5, "Цикли"), Assert.Single(result));
    }

    [Fact]
    public void Build_ListsSectionsInACycleOnce()
    {
        var result = SectionPaths.Build(new[] { S(1, "А", 2), S(2, "Б", 1), S(3, "В") });

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(o => o.Id).Order());
        Assert.Equal("В", result[0].Path);
    }

    [Fact]
    public void Build_ReturnsEmptyForNoSections()
        => Assert.Empty(SectionPaths.Build(Array.Empty<Section>()));
}
