using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.Categories;

namespace SchoolLibrary.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services in the dependency injection container.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();

        return services;
    }
}
