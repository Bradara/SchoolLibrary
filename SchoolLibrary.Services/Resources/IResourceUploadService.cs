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
}
