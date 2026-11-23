using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Favorites;

/// <summary>Keeps the resources each user saved as favorites.</summary>
public interface IFavoriteService
{
    /// <summary>
    /// Saves the resource as a favorite of the signed-in viewer. Saving it again is not an error.
    /// Fails when the resource does not exist or the viewer may not see it.
    /// </summary>
    Task<ServiceResult> AddAsync(ResourceViewer viewer, int resourceId);

    /// <summary>Removes the resource from the favorites. Removing one that is not saved is not an error.</summary>
    Task<ServiceResult> RemoveAsync(string userId, int resourceId);

    /// <summary>Which of the given resources the user has saved (for the hearts in a list).</summary>
    Task<HashSet<int>> GetFavoriteIdsAsync(string userId, IEnumerable<int> resourceIds);

    /// <summary>Whether the user saved this resource.</summary>
    Task<bool> IsFavoriteAsync(string userId, int resourceId);

    /// <summary>How many users saved this resource.</summary>
    Task<int> CountAsync(int resourceId);

    /// <summary>
    /// One page of the signed-in viewer's favorites, most recently saved first. Resources the owner has
    /// hidden from the viewer since they were saved are left out.
    /// </summary>
    Task<PagedResult<Resource>> GetFavoritesAsync(ResourceViewer viewer, int page, int pageSize = ResourceFilter.DefaultPageSize);
}
