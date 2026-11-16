using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Licenses;

/// <summary>Manages the licenses a resource can be shared under.</summary>
public interface ILicenseService
{
    /// <summary>All licenses in the order they were defined, for drop-downs.</summary>
    Task<IReadOnlyList<License>> GetOptionsAsync();

    /// <summary>All licenses ordered by name, each with the number of resources that use it.</summary>
    Task<IReadOnlyList<LicenseListItem>> GetAllAsync();

    /// <summary>A license, or null when it does not exist.</summary>
    Task<License?> GetByIdAsync(int id);

    /// <summary>Creates a license. Fails when the name is empty, too long or already used, or when a field is invalid.</summary>
    Task<ServiceResult<License>> CreateAsync(LicenseInput input);

    /// <summary>Changes a license. Fails when it does not exist or the new values are invalid or the name is used.</summary>
    Task<ServiceResult<License>> UpdateAsync(int id, LicenseInput input);

    /// <summary>Deletes a license. Fails when it does not exist or resources still use it.</summary>
    Task<ServiceResult<License>> DeleteAsync(int id);
}
