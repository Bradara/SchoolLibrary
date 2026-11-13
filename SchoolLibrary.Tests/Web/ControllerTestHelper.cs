using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace SchoolLibrary.Tests.Web;

/// <summary>Gives a controller what the framework gives it per request: a user, TempData and a URL helper.</summary>
public static class ControllerTestHelper
{
    /// <summary>A signed-in user with the roles, or an anonymous visitor when <paramref name="userId"/> is null.</summary>
    public static T WithUser<T>(this T controller, string? userId, params string[] roles) where T : Controller
    {
        var identity = new ClaimsIdentity();
        if (userId != null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            identity = new ClaimsIdentity(claims, authenticationType: "test");
        }

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var actionContext = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());

        controller.ControllerContext = new ControllerContext(actionContext);
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        controller.Url = new UrlHelper(actionContext);
        return controller;
    }

    public static string? TempDataText(this Controller controller, string key) => controller.TempData[key] as string;
}
