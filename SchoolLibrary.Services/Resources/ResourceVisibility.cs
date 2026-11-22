using System.Security.Claims;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Resources;

/// <summary>
/// Who is looking at the catalog: the user's id, the audience groups they belong to and whether they
/// are an administrator. Every query that shows resources is filtered with it.
/// </summary>
public sealed record ResourceViewer(string? UserId, ResourceAudience Audience, bool IsAdministrator)
{
    /// <summary>A visitor who is not signed in.</summary>
    public static ResourceViewer Anonymous { get; } = new(null, ResourceAudience.Guests, false);

    /// <summary>
    /// The viewer for a signed-in or anonymous user. A signed-in user also sees what guests see
    /// (they could sign out to see it anyway); a user without a role group (for example only "User")
    /// sees only that.
    /// </summary>
    public static ResourceViewer For(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Anonymous;
        }

        var audience = ResourceAudience.Guests;
        if (user.IsInRole(Roles.Student)) audience |= ResourceAudience.Students;
        if (user.IsInRole(Roles.Parent)) audience |= ResourceAudience.Parents;
        if (user.IsInRole(Roles.Teacher)) audience |= ResourceAudience.Teachers;

        return new ResourceViewer(
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            audience,
            user.IsInRole(Roles.Administrator));
    }
}

/// <summary>The rule "who may see a resource": the owner, an administrator, or a member of a chosen group.</summary>
public static class ResourceVisibility
{
    /// <summary>Only the known flags; anything else that came with a form is dropped.</summary>
    public static ResourceAudience Clean(ResourceAudience audience) => audience & ResourceAudience.Everyone;

    /// <summary>The audience made of the groups ticked in a form.</summary>
    public static ResourceAudience Combine(IEnumerable<ResourceAudience>? groups) =>
        Clean((groups ?? Enumerable.Empty<ResourceAudience>()).Aggregate(ResourceAudience.None, (all, g) => all | g));

    public static bool CanView(Resource resource, ResourceViewer viewer) =>
        viewer.IsAdministrator
        || (viewer.UserId != null && resource.OwnerId == viewer.UserId)
        || (resource.Audience & viewer.Audience) != ResourceAudience.None;

    /// <summary>The same rule as <see cref="CanView"/>, translated to SQL.</summary>
    public static IQueryable<Resource> VisibleTo(this IQueryable<Resource> query, ResourceViewer viewer)
    {
        if (viewer.IsAdministrator)
        {
            return query;
        }

        var audience = viewer.Audience;
        var userId = viewer.UserId;
        return userId == null
            ? query.Where(r => (r.Audience & audience) != ResourceAudience.None)
            : query.Where(r => (r.Audience & audience) != ResourceAudience.None || r.OwnerId == userId);
    }

    /// <summary>The groups (in display order) with their Bulgarian names.</summary>
    public static IReadOnlyList<(ResourceAudience Group, string Name)> Groups { get; } = new[]
    {
        (ResourceAudience.Guests, "Гости (без вход)"),
        (ResourceAudience.Students, "Ученици"),
        (ResourceAudience.Parents, "Родители"),
        (ResourceAudience.Teachers, "Учители")
    };

    /// <summary>A short description of an audience for badges and hints.</summary>
    public static string Describe(ResourceAudience audience)
    {
        audience = Clean(audience);
        return audience switch
        {
            ResourceAudience.Everyone => "Всички",
            ResourceAudience.SignedIn => "Само регистрирани",
            ResourceAudience.None => "Само аз",
            _ => string.Join(", ", Groups.Where(g => audience.HasFlag(g.Group)).Select(g => g.Name))
        };
    }
}
