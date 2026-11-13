using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourcePermissionsTests
{
    private static Resource OwnedBy(string? ownerId) => new() { OwnerId = ownerId };

    [Theory]
    [InlineData("ivan", "ivan", false, true)]    // the owner edits
    [InlineData("ivan", "ivan", true, true)]     // an administrator who owns it edits
    [InlineData("ivan", "maria", false, false)]  // another user does not
    [InlineData("ivan", "maria", true, false)]   // an administrator does not edit other people's resources
    [InlineData("ivan", null, false, false)]     // anonymous visitor
    [InlineData(null, "maria", false, false)]    // no owner: a teacher cannot edit
    [InlineData(null, "maria", true, true)]      // no owner: an administrator can
    [InlineData(null, null, false, false)]
    public void CanEdit_FollowsOwnershipAndRole(string? ownerId, string? userId, bool isAdministrator, bool expected)
    {
        Assert.Equal(expected, ResourcePermissions.CanEdit(OwnedBy(ownerId), userId, isAdministrator));
    }

    [Theory]
    [InlineData("ivan", "ivan", false, true)]
    [InlineData("ivan", "maria", false, false)]
    [InlineData("ivan", "maria", true, true)]
    [InlineData(null, "maria", false, false)]
    [InlineData(null, "maria", true, true)]
    [InlineData("ivan", null, false, false)]
    public void CanDelete_OwnerOrAdministrator(string? ownerId, string? userId, bool isAdministrator, bool expected)
    {
        Assert.Equal(expected, ResourcePermissions.CanDelete(OwnedBy(ownerId), userId, isAdministrator));
    }
}
