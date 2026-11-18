namespace SchoolLibrary.Services.Resources;

/// <summary>
/// A file received for upload, described independently of ASP.NET so the service stays web-agnostic.
/// </summary>
public sealed class UploadedFile
{
    public UploadedFile(string fileName, string? contentType, long length, Stream content)
    {
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        Content = content;
    }

    public string FileName { get; }

    /// <summary>The type the client declared; not trusted — the real type is checked from the bytes.</summary>
    public string? ContentType { get; }

    public long Length { get; }

    public Stream Content { get; }
}

/// <summary>Limits and allowed formats for uploaded resource files.</summary>
public static class UploadConstraints
{
    public const int MaxFileSizeMegabytes = 25;

    public const long MaxFileSizeBytes = (long)MaxFileSizeMegabytes * 1024 * 1024;

    /// <summary>Extensions accepted by the upload form (used for the input's "accept" attribute).</summary>
    public static readonly IReadOnlyList<string> AllowedExtensions = new[] { ".pdf", ".epub", ".md", ".markdown" };
}
