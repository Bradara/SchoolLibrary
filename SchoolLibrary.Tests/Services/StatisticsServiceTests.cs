using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Statistics;

namespace SchoolLibrary.Tests.Services;

public class StatisticsServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_EmptyDatabase_ReturnsZeros()
    {
        using var context = TestDbContextFactory.Create();
        var service = new StatisticsService(context);

        var result = await service.GetDashboardAsync();

        Assert.Equal(new DashboardStatistics(0, 0, 0, 0, 0), result);
    }

    [Fact]
    public async Task GetDashboardAsync_CountsEntitiesAndPendingTeachers()
    {
        using var context = TestDbContextFactory.Create();
        context.Users.AddRange(
            new ApplicationUser { UserName = "a", TeacherRequested = true },
            new ApplicationUser { UserName = "b", TeacherRequested = true },
            new ApplicationUser { UserName = "c" });
        context.Categories.Add(new Category { Name = "Музика" });
        context.Resources.Add(new Resource
        {
            Title = "Нотация",
            Url = "https://example.com",
            Author = "Автор",
            CreatedOn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new StatisticsService(context);

        var result = await service.GetDashboardAsync();

        Assert.Equal(3, result.Users);
        Assert.Equal(1, result.Resources);
        Assert.Equal(1, result.Categories);
        Assert.Equal(2, result.PendingTeachers);
    }

    [Fact]
    public async Task GetDashboardAsync_CountsOnlyNewReports()
    {
        using var context = TestDbContextFactory.Create();
        context.Reports.AddRange(
            new Report { TargetTitle = "А", Status = ReportStatus.New, CreatedOn = DateTime.UtcNow },
            new Report { TargetTitle = "Б", Status = ReportStatus.New, CreatedOn = DateTime.UtcNow },
            new Report { TargetTitle = "В", Status = ReportStatus.Reviewed, CreatedOn = DateTime.UtcNow },
            new Report { TargetTitle = "Г", Status = ReportStatus.Rejected, CreatedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();
        var service = new StatisticsService(context);

        var result = await service.GetDashboardAsync();

        Assert.Equal(2, result.NewReports);
    }
}
