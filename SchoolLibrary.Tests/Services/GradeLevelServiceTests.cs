using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.GradeLevels;

namespace SchoolLibrary.Tests.Services;

public class GradeLevelServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsGradesOrderedByNumber()
    {
        using var context = TestDbContextFactory.Create();
        context.GradeLevels.AddRange(
            new GradeLevel { Number = 9 },
            new GradeLevel { Number = 5 },
            new GradeLevel { Number = 12 });
        await context.SaveChangesAsync();
        var service = new GradeLevelService(context);

        var result = await service.GetAllAsync();

        Assert.Equal(new[] { 5, 9, 12 }, result.Select(g => g.Number));
    }

    [Fact]
    public async Task GetAllAsync_WithNoGrades_ReturnsEmptyList()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeLevelService(context);

        Assert.Empty(await service.GetAllAsync());
    }
}
