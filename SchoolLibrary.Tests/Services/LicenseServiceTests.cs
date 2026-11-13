using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Licenses;

namespace SchoolLibrary.Tests.Services;

public class LicenseServiceTests
{
    [Fact]
    public async Task GetOptionsAsync_ReturnsLicensesInDefinedOrder()
    {
        using var context = TestDbContextFactory.Create();
        context.Licenses.Add(new License { Id = 1, Name = "Собствен материал" });
        context.Licenses.Add(new License { Id = 2, Name = "CC BY 4.0" });
        await context.SaveChangesAsync();
        var service = new LicenseService(context);

        var result = await service.GetOptionsAsync();

        Assert.Equal(new[] { "Собствен материал", "CC BY 4.0" }, result.Select(l => l.Name));
    }

    [Fact]
    public async Task GetOptionsAsync_WithNoLicenses_ReturnsEmptyList()
    {
        using var context = TestDbContextFactory.Create();
        var service = new LicenseService(context);

        Assert.Empty(await service.GetOptionsAsync());
    }
}
