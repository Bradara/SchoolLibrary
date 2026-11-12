using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services in the dependency injection container.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IGradeLevelService, GradeLevelService>();
        services.AddScoped<IResourceService, ResourceService>();

        return services;
    }
}
