using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

/// <summary>Creates uploaded resources: validates the file and the form fields, then stores both.</summary>
public interface IResourceUploadService
{
    /// <summary>
    /// Validates the file (type, size, content) and the form fields, stores the file through the file
    /// storage and creates an uploaded resource with its <see cref="ResourceFile"/>. An EPUB keeps the cover from
    /// inside the book; a PDF keeps <paramref name="pdfCover"/>, the first page drawn by the browser, when it is an
    /// accepted picture (otherwise the PDF is uploaded without a cover).
    /// </summary>
    Task<ServiceResult<Resource>> UploadAsync(
        ResourceInput input, UploadedFile file, string? ownerId, UploadedFile? pdfCover = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a new version of the file in place of the old one: the same checks as an upload (type, size, the same
    /// file nowhere else in the library), and the cover comes from the new file. The old file and its cover are
    /// removed from the storage only after the database points to the new ones. The details of the resource
    /// (title, subjects, …), its comments, favorites and reading positions stay.
    /// </summary>
    Task<ServiceResult<Resource>> ReplaceFileAsync(
        int resourceId, UploadedFile file, string userId, bool isAdministrator, UploadedFile? pdfCover = null,
        CancellationToken cancellationToken = default);
}
