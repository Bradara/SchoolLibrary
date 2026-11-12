using System.Security.Claims;
using SchoolLibrary.Data.Constants;

namespace SchoolLibrary.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>True for teachers and administrators, the only roles that may add resources.</summary>
    public static bool CanAddResources(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Teacher) || user.IsInRole(Roles.Administrator);
}
