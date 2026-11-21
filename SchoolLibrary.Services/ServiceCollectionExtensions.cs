using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Markdown;
using SchoolLibrary.Services.OnlineLibraries;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.SiteIcons;
using SchoolLibrary.Services.Statistics;
using SchoolLibrary.Services.Storage;
using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services in the dependency injection container.</summary>
    /// <param name="youTubeApiKey">YouTube Data API key; without one YouTube is not offered in "Find resources".</param>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, string? youTubeApiKey = null)
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
        services.AddScoped<ISectionService, SectionService>();
        services.AddScoped<IQuestionBankService, QuestionBankService>();
        services.AddScoped<IQuestionExportService, QuestionExportService>();
        services.AddScoped<IQuizItemService, QuizItemService>();
        services.AddScoped<ISchoolClassService, SchoolClassService>();
        services.AddScoped<IExamAssignmentService, ExamAssignmentService>();

        // Default file storage; Program.cs replaces it with the configured path (a mounted volume on the
        // server). The folder is created lazily on the first upload, so this default touches no disk.
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(
            Path.Combine(AppContext.BaseDirectory, "App_Data", "uploads")));

        services.AddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();

        // Sources for "Find resources". The hosts are fixed in code: the search never fetches an address a user typed
        foreach (var clientName in new[] { WikimediaSourceProvider.HttpClientName, PhetSourceProvider.HttpClientName, GutenbergSourceProvider.HttpClientName, InternetArchiveSourceProvider.HttpClientName, GeoGebraSourceProvider.HttpClientName, ScratchSourceProvider.HttpClientName, YouTubeSourceProvider.HttpClientName })
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
        services.AddTransient<IResourceSourceProvider, GeoGebraSourceProvider>();
        services.AddTransient<IResourceSourceProvider, ScratchSourceProvider>();

        // YouTube needs an API key (user secrets or an environment variable, never the repository)
        if (!string.IsNullOrWhiteSpace(youTubeApiKey))
        {
            services.AddTransient<IResourceSourceProvider>(sp => new YouTubeSourceProvider(
                sp.GetRequiredService<IHttpClientFactory>(), youTubeApiKey));
        }

        // Site icons of the online libraries. The address is typed by an administrator, so every
        // connection goes through the guard that allows only public addresses and web ports.
        services.AddHttpClient(SiteIconFetcher.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SchoolLibraryBot/1.0)");
            })
            .ConfigurePrimaryHttpMessageHandler(PublicNetworkGuard.CreateHandler);
        services.AddTransient<ISiteIconFetcher, SiteIconFetcher>();
        services.AddScoped<ILibraryIconService, LibraryIconService>();

        return services;
    }

    /// <summary>
    /// Registers the sandbox server that checks code questions. Without a server address nothing is
    /// registered and code questions are not offered.
    /// </summary>
    public static IServiceCollection AddCodeRunner(this IServiceCollection services, CodeRunnerOptions? options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return services;
        }

        var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
        services.AddHttpClient(JobeCodeRunner.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.CpuTimeSeconds + 40); // compiling + every input + queueing
        });
        services.AddTransient<ICodeRunner>(sp => new JobeCodeRunner(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(JobeCodeRunner.HttpClientName), options));

        return services;
    }
}
