using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Data;
using SchoolLibrary.Services;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ServiceRegistrationTests
{
    private static ServiceProvider BuildProvider(bool validate = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddApplicationServices();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validate, ValidateOnBuild = validate });
    }

    // Every interface the services project defines. A new service without a registration would only
    // fail when the page that needs it is opened; this test fails first.
    private static readonly Type[] ServiceInterfaces = typeof(IResourceService).Assembly.GetTypes()
        .Where(t => t.IsInterface && t.Name.EndsWith("Service") && t.Namespace!.StartsWith("SchoolLibrary.Services"))
        .ToArray();

    [Fact]
    public void ThereAreServicesToCheck()
    {
        Assert.True(ServiceInterfaces.Length >= 9, "found: " + string.Join(", ", ServiceInterfaces.Select(t => t.Name)));
    }

    [Fact]
    public void EveryServiceInterface_CanBeResolved()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        foreach (var serviceType in ServiceInterfaces)
        {
            var service = scope.ServiceProvider.GetService(serviceType);
            Assert.True(service != null, serviceType.Name + " is not registered");
            Assert.True(serviceType.IsInstanceOfType(service));
        }
    }

    [Fact]
    public void Services_AreScoped_SoOneRequestSharesTheDbContextAndNothingIsSharedBetweenRequests()
    {
        using var provider = BuildProvider();

        object First(IServiceProvider p) => p.GetRequiredService<IResourceService>();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        Assert.Same(First(scopeA.ServiceProvider), First(scopeA.ServiceProvider));
        Assert.NotSame(First(scopeA.ServiceProvider), First(scopeB.ServiceProvider));
    }

    [Fact]
    public void Services_CannotBeResolvedOutsideAScope()
    {
        using var provider = BuildProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IResourceService>());
    }

    [Fact]
    public void ServicesThatDependOnEachOther_ResolveTogether()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // The import and the report services are built from other services
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IResourceImportService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SchoolLibrary.Services.Reports.IReportService>());
    }

    [Fact]
    public void AddApplicationServices_ReturnsTheCollection_ForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddApplicationServices());
    }
}
