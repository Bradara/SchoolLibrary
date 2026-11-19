using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Markdown;
using SchoolLibrary.Services.OnlineLibraries;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Statistics;
using SchoolLibrary.Services.Storage;
using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services in the dependency injection container.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IGradeLevelService, GradeLevelService>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IOnlineLibraryService, OnlineLibraryService>();
        services.AddScoped<IQuizService, QuizService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IResourceImportService, ResourceImportService>();
        services.AddScoped<IResourceUploadService, ResourceUploadService>();
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IResourceDiscoveryService, ResourceDiscoveryService>();

        // Default file storage; Program.cs replaces it with the configured path (a mounted volume on the
        // server). The folder is created lazily on the first upload, so this default touches no disk.
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(
            Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads")));

        services.AddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();

        // Sources for "Find resources". The hosts are fixed in code: the search never fetches an address a user typed
        foreach (var clientName in new[] { WikimediaSourceProvider.HttpClientName, PhetSourceProvider.HttpClientName, GutenbergSourceProvider.HttpClientName, InternetArchiveSourceProvider.HttpClientName })
        {
            services.AddHttpClient(clientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SchoolLibraryBot/1.0");
            });
        }

        foreach (var wiki in WikimediaSourceProvider.Wikis)
        {
            services.AddTransient<IResourceSourceProvider>(sp => new WikimediaSourceProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(WikimediaSourceProvider.HttpClientName), wiki));
        }

        // One instance for the whole application: the PhET catalog is downloaded once and kept in memory
        services.AddSingleton<IResourceSourceProvider, PhetSourceProvider>();
        services.AddTransient<IResourceSourceProvider, GutenbergSourceProvider>();
        services.AddTransient<IResourceSourceProvider, InternetArchiveSourceProvider>();

        return services;
    }
}
