using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Storage;

/// <summary>
/// Keeps uploaded files in a folder on disk (a mounted volume in Docker). Suitable for a single
/// application instance; shared object storage can replace it later behind <see cref="IFileStorage"/>.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    // A key is a relative path made of safe characters, e.g. "resources/ab12.pdf": it must start with a
    // letter or digit and may not contain a drive, a backslash or "..", so a crafted key can never escape
    // the root folder.
    private static readonly Regex SafeKey = new(@"^[A-Za-z0-9][A-Za-z0-9/_.-]*$", RegexOptions.Compiled);

    private readonly string _root;

    public LocalFileStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("The storage root path is required.", nameof(rootPath));
        }

        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(ResolvePath(key)));
    }

    // Turns a key into a full path inside the root and refuses anything that could leave the root
    private string ResolvePath(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains("..") || key.EndsWith('/') || !SafeKey.IsMatch(key))
        {
            throw new ArgumentException($"Invalid storage key: '{key}'.", nameof(key));
        }

        var full = Path.GetFullPath(Path.Combine(_root, key));

        // Defence in depth: the resolved path must still sit under the root folder
        var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid storage key: '{key}'.", nameof(key));
        }

        return full;
    }
}
