using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

/// <summary>Business rules for learning resources: listing, validation, ownership.</summary>
public interface IResourceService
{
    /// <summary>One page of resources matching the filter, in the requested order.</summary>
    Task<PagedResult<Resource>> SearchAsync(ResourceFilter filter);

    /// <summary>Users who published at least one resource, ordered by name.</summary>
    Task<IReadOnlyList<ResourceOwnerOption>> GetOwnersAsync();

    /// <summary>A resource with its subjects, grades and owner, or null when it does not exist.</summary>
    Task<Resource?> GetDetailsAsync(int id);

    /// <summary>Loads a resource for editing. Only its owner may edit it.</summary>
    Task<ServiceResult<Resource>> GetForEditAsync(int id, string userId);

    /// <summary>Loads a resource for deleting. Its owner or an administrator may delete it.</summary>
    Task<ServiceResult<Resource>> GetForDeleteAsync(int id, string userId, bool isAdministrator);

    /// <summary>Validates the input and creates a resource owned by <paramref name="ownerId"/>.</summary>
    Task<ServiceResult<Resource>> CreateAsync(ResourceInput input, string ownerId);

    /// <summary>Validates the input and updates the resource. Only its owner may update it.</summary>
    Task<ServiceResult<Resource>> UpdateAsync(int id, ResourceInput input, string userId);

    /// <summary>Deletes the resource. Its owner or an administrator may delete it.</summary>
    Task<ServiceResult<Resource>> DeleteAsync(int id, string userId, bool isAdministrator);
}
