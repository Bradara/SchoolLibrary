namespace SchoolLibrary.Services.Storage;

/// <summary>
/// Stores uploaded files outside the database. The key identifies a file (for example
/// "resources/{guid}.pdf"); only the key and the file metadata live in the database, the bytes in the
/// storage backend. A later implementation (OCI Object Storage) can replace the local one without
/// changing the callers.
/// </summary>
public interface IFileStorage
{
    /// <summary>Writes the content under the given key, replacing any existing file with that key.</summary>
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens the file for reading, or returns null when no file has that key.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes the file; does nothing when no file has that key.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True when a file is stored under the key.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
