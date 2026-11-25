namespace SchoolLibrary.Services.Resources;

/// <summary>What happened to the books looked at by <see cref="IResourceCoverService.ExtractMissingAsync"/>.</summary>
/// <param name="Checked">Books without a cover that were looked at.</param>
/// <param name="Added">Books that got a cover.</param>
/// <param name="WithoutCover">Books with no usable cover picture inside.</param>
/// <param name="FileMissing">Books whose file is not in the storage (or is larger than any upload may be).</param>
public record CoverExtractionResult(int Checked, int Added, int WithoutCover, int FileMissing);

/// <summary>Covers for books uploaded before covers were kept.</summary>
public interface IResourceCoverService
{
    /// <summary>How many uploaded EPUB books have no cover yet.</summary>
    Task<int> CountBooksWithoutCoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the cover out of every uploaded EPUB that has none and stores it. Each book is saved on its own, so the
    /// books done before a failure keep their covers.
    /// </summary>
    Task<CoverExtractionResult> ExtractMissingAsync(CancellationToken cancellationToken = default);
}
