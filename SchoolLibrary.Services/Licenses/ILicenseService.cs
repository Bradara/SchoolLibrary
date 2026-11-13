using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Licenses;

/// <summary>Reads the licenses a resource can be shared under.</summary>
public interface ILicenseService
{
    /// <summary>All licenses in the order they were defined, for drop-downs.</summary>
    Task<IReadOnlyList<License>> GetOptionsAsync();
}
