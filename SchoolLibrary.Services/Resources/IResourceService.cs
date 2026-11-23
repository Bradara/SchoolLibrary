using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

/// <summary>Business rules for learning resources: listing, validation, ownership.</summary>
public interface IResourceService
{
    /// <summary>One page of resources matching the filter, in the requested order.</summary>
    Task<PagedResult<Resource>> SearchAsync(ResourceFilter filter);

    /// <summary>
    /// The counts for the kind switch and the subject tiles. The kind counts ignore the chosen kind, the subject
    /// counts ignore the chosen subject; all other filters apply. Subjects without resources are left out.
    /// Paging and sorting are ignored.
    /// </summary>
    Task<CatalogFacets> GetCatalogFacetsAsync(ResourceFilter filter);

    /// <summary>Users who published at least one resource, ordered by name.</summary>
    Task<IReadOnlyList<ResourceOwnerOption>> GetOwnersAsync(ResourceViewer viewer);

    /// <summary>A resource with its subjects, grades and owner, or null when it does not exist or the viewer may not see it.</summary>
    Task<Resource?> GetDetailsAsync(int id, ResourceViewer viewer);

    /// <summary>Whether the resource exists and the viewer may see it.</summary>
    Task<bool> CanViewAsync(int id, ResourceViewer viewer);

    /// <summary>The uploaded file of a resource, or null when it has none or the viewer may not see it.</summary>
    Task<ResourceFile?> GetFileAsync(int resourceId, ResourceViewer viewer);

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

    /// <summary>Changes who may see the resource. Same rule as <see cref="GetForEditAsync"/>.</summary>
    Task<ServiceResult<Resource>> SetAudienceAsync(int id, ResourceAudience audience, string userId, bool isAdministrator);

    /// <summary>Deletes the resource. Its owner or an administrator may delete it.</summary>
    Task<ServiceResult<Resource>> DeleteAsync(int id, string userId, bool isAdministrator);
}
