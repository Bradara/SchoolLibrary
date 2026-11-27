using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

/// <summary>What happened to the books looked at by <see cref="IResourceCoverService.ExtractMissingAsync"/>.</summary>
/// <param name="Checked">Books without a cover that were looked at.</param>
/// <param name="Added">Books that got a cover.</param>
/// <param name="WithoutCover">Books with no usable cover picture inside.</param>
/// <param name="FileMissing">Books whose file is not in the storage (or is larger than any upload may be).</param>
public record CoverExtractionResult(int Checked, int Added, int WithoutCover, int FileMissing);

/// <summary>A PDF without a cover, for the administrator's browser to draw its first page.</summary>
/// <param name="ResourceId">The uploaded resource.</param>
/// <param name="Title">Its title, shown while the covers are made.</param>
public record PdfWithoutCover(int ResourceId, string Title);

/// <summary>Covers for books and PDF files uploaded before covers were kept.</summary>
public interface IResourceCoverService
{
    /// <summary>How many uploaded EPUB books have no cover yet.</summary>
    Task<int> CountBooksWithoutCoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the cover out of every uploaded EPUB that has none and stores it. Each book is saved on its own, so the
    /// books done before a failure keep their covers.
    /// </summary>
    Task<CoverExtractionResult> ExtractMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>How many uploaded PDF files have no cover yet.</summary>
    Task<int> CountPdfsWithoutCoverAsync(CancellationToken cancellationToken = default);

    /// <summary>The uploaded PDF files without a cover, oldest first.</summary>
    Task<IReadOnlyList<PdfWithoutCover>> GetPdfsWithoutCoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the first page of a PDF, drawn by the administrator's browser, as its cover. Refused when the
    /// resource is not an uploaded PDF (NotFound), already has a cover (Conflict) or the picture is not a PNG,
    /// JPEG or WebP of at most <see cref="ResourceCovers.MaxPdfCoverBytes"/> (Invalid).
    /// </summary>
    Task<ServiceResult> SetPdfCoverAsync(int resourceId, byte[]? picture, CancellationToken cancellationToken = default);
}
