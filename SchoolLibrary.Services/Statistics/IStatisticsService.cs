namespace SchoolLibrary.Services.Statistics;

/// <summary>Totals shown on the administration dashboard.</summary>
public record DashboardStatistics(int Users, int Resources, int Categories, int PendingTeachers);

public interface IStatisticsService
{
    Task<DashboardStatistics> GetDashboardAsync();
}
