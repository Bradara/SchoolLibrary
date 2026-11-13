using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Resources;

/// <summary>Who may change a resource. Used by the service and by the views that show the buttons.</summary>
public static class ResourcePermissions
{
    /// <summary>
    /// The owner may edit. A resource without an owner (an imported free resource) has nobody to edit it,
    /// so administrators may. Administrators may not edit other people's resources.
    /// </summary>
    public static bool CanEdit(Resource resource, string? userId, bool isAdministrator)
    {
        if (resource.OwnerId == null)
        {
            return isAdministrator;
        }

        return userId != null && resource.OwnerId == userId;
    }

    /// <summary>The owner or an administrator may delete.</summary>
    public static bool CanDelete(Resource resource, string? userId, bool isAdministrator) =>
        isAdministrator || (userId != null && resource.OwnerId == userId);
}
