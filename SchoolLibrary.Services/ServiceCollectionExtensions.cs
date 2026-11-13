using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Statistics;
using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services in the dependency injection container.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IGradeLevelService, GradeLevelService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IResourceImportService, ResourceImportService>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
