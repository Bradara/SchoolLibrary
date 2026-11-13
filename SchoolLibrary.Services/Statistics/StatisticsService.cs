using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly ApplicationDbContext _context;

    public StatisticsService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardStatistics> GetDashboardAsync()
    {
        // Sequential awaits: one DbContext cannot run queries in parallel
        var users = await _context.Users.CountAsync();
        var resources = await _context.Resources.CountAsync();
        var categories = await _context.Categories.CountAsync();
        var pendingTeachers = await _context.Users.CountAsync(u => u.TeacherRequested);

        var newReports = await _context.Reports.CountAsync(r => r.Status == ReportStatus.New);

        return new DashboardStatistics(users, resources, categories, pendingTeachers, newReports);
    }
}
