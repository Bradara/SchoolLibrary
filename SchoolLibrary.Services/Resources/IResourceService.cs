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

    /// <summary>The resource that already uses this link (in any spelling), or null when the link is new.</summary>
    Task<Resource?> FindByUrlAsync(string url);

    /// <summary>Loads a resource for editing. Its owner may edit it; administrators may edit resources without an owner.</summary>
    Task<ServiceResult<Resource>> GetForEditAsync(int id, string userId, bool isAdministrator);

    /// <summary>Loads a resource for deleting. Its owner or an administrator may delete it.</summary>
    Task<ServiceResult<Resource>> GetForDeleteAsync(int id, string userId, bool isAdministrator);

    /// <summary>Validates the input and creates a resource owned by <paramref name="ownerId"/> (null for imported resources).</summary>
    Task<ServiceResult<Resource>> CreateAsync(ResourceInput input, string? ownerId);

    /// <summary>Validates the input and updates the resource. Same rule as <see cref="GetForEditAsync"/>.</summary>
    Task<ServiceResult<Resource>> UpdateAsync(int id, ResourceInput input, string userId, bool isAdministrator);

    /// <summary>Deletes the resource. Its owner or an administrator may delete it.</summary>
    Task<ServiceResult<Resource>> DeleteAsync(int id, string userId, bool isAdministrator);
}
