using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Middleware;
using PublicHomeController = SchoolLibrary.Controllers.HomeController;

namespace SchoolLibrary.Tests.Web;

public class SecurityTests
{
    // ---------- response headers ----------

    private static async Task<IHeaderDictionary> RunMiddlewareAsync()
    {
        var context = new DefaultHttpContext();
        var called = false;
        var middleware = new SecurityHeadersMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(called, "the rest of the pipeline must still run");
        return context.Response.Headers;
    }

    [Fact]
    public async Task Middleware_SetsTheProtectiveHeaders()
    {
        var headers = await RunMiddlewareAsync();

        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.Contains("camera=()", headers["Permissions-Policy"].ToString());
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, headers["Content-Security-Policy"]);
    }

    [Fact]
    public void ContentSecurityPolicy_ForbidsFramingAndPlugins_ButDoesNotRestrictScriptsOrFrames()
    {
        var policy = SecurityHeadersMiddleware.ContentSecurityPolicy;

        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("base-uri 'self'", policy);
        Assert.Contains("form-action 'self'", policy);

        // The pages use inline scripts and show other sites in iframes: these must stay unrestricted
        Assert.DoesNotContain("default-src", policy);
        Assert.DoesNotContain("script-src", policy);
        Assert.DoesNotContain("frame-src", policy);
        Assert.DoesNotContain("child-src", policy);
    }

    [Fact]
    public async Task Middleware_ReplacesAHeaderSetEarlier()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers["X-Frame-Options"] = "ALLOWALL";
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
    }

    // ---------- conventions every controller must keep ----------

    private static readonly Assembly WebAssembly = typeof(PublicHomeController).Assembly;

    private static IEnumerable<Type> Controllers() =>
        WebAssembly.GetTypes().Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool Writes(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().Any(a => a.HttpMethods.Any(m => m != "GET" && m != "HEAD"));

    [Fact]
    public void EveryActionThatChangesData_ChecksTheAntiforgeryToken()
    {
        var unprotected = Controllers()
            .SelectMany(c => Actions(c).Where(Writes).Select(a => (Name: $"{c.Name}.{a.Name}", Action: a)))
            .Where(x => x.Action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() == null)
            .Select(x => x.Name)
            .ToList();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void TheControllersHaveActionsThatChangeData_SoTheCheckAboveIsNotEmpty()
    {
        Assert.True(Controllers().SelectMany(Actions).Count(Writes) >= 15);
    }

    [Fact]
    public void NoActionThatChangesData_IsOpenToAnonymousUsers()
    {
        var open = Controllers()
            .SelectMany(c => Actions(c).Where(Writes).Where(a => a.GetCustomAttribute<AllowAnonymousAttribute>() != null)
                .Select(a => $"{c.Name}.{a.Name}"))
            .ToList();

        Assert.Empty(open);
    }

    [Fact]
    public void EveryController_ExceptTheHomePage_RequiresASignedInUser()
    {
        var open = Controllers()
            .Where(c => c != typeof(PublicHomeController))
            .Where(c => c.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Count() == 0)
            .Select(c => c.Name)
            .ToList();

        Assert.Empty(open);
    }

    [Fact]
    public void EveryAdminAreaController_RequiresTheAdministratorRole()
    {
        var adminControllers = Controllers()
            .Where(c => c.Namespace == typeof(AdminController).Namespace && c != typeof(AdminController))
            .ToList();

        Assert.NotEmpty(adminControllers);
        Assert.All(adminControllers, c =>
        {
            Assert.True(typeof(AdminController).IsAssignableFrom(c), c.Name + " must derive from AdminController");
            var roles = c.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Roles);
            Assert.Contains(Roles.Administrator, roles);
        });
    }

    [Fact]
    public void ResourcesThatAnonymousVisitorsMayRead_AreOnlyTheListAndTheDetails()
    {
        var anonymous = typeof(ResourcesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name)
            .OrderBy(n => n);

        Assert.Equal(new[] { "Details", "Index" }, anonymous);
    }
}
