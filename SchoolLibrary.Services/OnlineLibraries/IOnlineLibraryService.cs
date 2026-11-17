using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.OnlineLibraries;

/// <summary>Manages the catalog of external online libraries.</summary>
public interface IOnlineLibraryService
{
    /// <summary>All libraries in the order they are shown: by sort number, then by name.</summary>
    Task<IReadOnlyList<OnlineLibrary>> GetAllAsync();

    /// <summary>A library, or null when it does not exist.</summary>
    Task<OnlineLibrary?> GetByIdAsync(int id);

    /// <summary>The icons that can be chosen for a card.</summary>
    IReadOnlyList<LibraryIconOption> GetIcons();

    /// <summary>Adds a library. Fails when a field is invalid or the name or address is already in the catalog.</summary>
    Task<ServiceResult<OnlineLibrary>> CreateAsync(OnlineLibraryInput input);

    /// <summary>Changes a library. Fails when it does not exist, a field is invalid or the name or address is used by another one.</summary>
    Task<ServiceResult<OnlineLibrary>> UpdateAsync(int id, OnlineLibraryInput input);

    /// <summary>Removes a library from the catalog. Fails when it does not exist.</summary>
    Task<ServiceResult<OnlineLibrary>> DeleteAsync(int id);
}
