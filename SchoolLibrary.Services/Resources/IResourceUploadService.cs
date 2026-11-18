using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

/// <summary>Creates uploaded resources: validates the file and the form fields, then stores both.</summary>
public interface IResourceUploadService
{
    /// <summary>
    /// Validates the file (type, size, content) and the form fields, stores the file through the file
    /// storage and creates an uploaded resource with its <see cref="ResourceFile"/>.
    /// </summary>
    Task<ServiceResult<Resource>> UploadAsync(
        ResourceInput input, UploadedFile file, string? ownerId, CancellationToken cancellationToken = default);
}
