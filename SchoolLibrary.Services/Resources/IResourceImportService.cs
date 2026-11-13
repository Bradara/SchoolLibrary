namespace SchoolLibrary.Services.Resources;

/// <summary>
/// Adds free resources (<c>ResourceKind.External</c>, without an owner) from a CSV file.
/// The file uses ';' as separator and these columns: title, author, url, license, subjects, grades
/// (required) and source, description (optional). Several subjects or grades in one cell are separated by '|'.
/// </summary>
public interface IResourceImportService
{
    /// <summary>The most data rows one file may have.</summary>
    const int MaxRows = 500;

    /// <summary>The largest accepted file text, in characters.</summary>
    const int MaxFileLength = 512 * 1024;

    /// <summary>Checks every row and reports what an import would do. Nothing is saved.</summary>
    Task<ResourceImportPreview> PreviewAsync(string csvText);

    /// <summary>
    /// Checks the file again and saves the rows that are new. Invalid rows and duplicates are skipped
    /// and reported; nothing is saved when the file itself cannot be read.
    /// </summary>
    Task<ResourceImportPreview> ImportAsync(string csvText);
}
