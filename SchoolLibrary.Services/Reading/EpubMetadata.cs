using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Services.Reading;

/// <summary>The title, the author and the cover picture found in an EPUB book. Any part may be missing.</summary>
public sealed record EpubInfo(string? Title, string? Author, EpubCover? Cover)
{
    public static readonly EpubInfo Empty = new(null, null, null);
}

/// <summary>A cover picture taken out of an EPUB; its type was recognized from the bytes.</summary>
public sealed record EpubCover(byte[] Bytes, string ContentType, string Extension);

/// <summary>
/// Reads the title, the author and the cover of an EPUB (a ZIP with META-INF/container.xml pointing to the package
/// document). Never throws for a broken or hostile book: whatever cannot be read safely is left out.
/// </summary>
public static class EpubMetadata
{
    /// <summary>The largest cover kept; a bigger picture is left out (the card shows a generated cover).</summary>
    public const int MaxCoverBytes = 1024 * 1024;

    // container.xml and the package document are small; a bigger entry is not read (zip bomb)
    private const int MaxXmlBytes = 1024 * 1024;

    private const int MaxTextLength = 100;

    private const string ContainerPath = "META-INF/container.xml";

    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    public static EpubInfo Read(byte[] epub)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(epub, writable: false), ZipArchiveMode.Read);

            var container = LoadXml(zip, ContainerPath);
            var opfPath = container?
                .Descendants(ContainerNs + "rootfile")
                .Select(r => (string?)r.Attribute("full-path"))
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (opfPath == null)
            {
                return EpubInfo.Empty;
            }

            var opf = LoadXml(zip, opfPath);
            if (opf == null)
            {
                return EpubInfo.Empty;
            }

            var metadata = opf.Descendants(OpfNs + "metadata").FirstOrDefault();
            var title = metadata?.Elements(DcNs + "title").Select(e => Clean(e.Value)).FirstOrDefault(t => t != null);
            var authors = metadata?.Elements(DcNs + "creator").Select(e => Clean(e.Value)).OfType<string>().Distinct().ToList();
            var author = authors is { Count: > 0 } ? Clean(string.Join(", ", authors)) : null;

            return new EpubInfo(title, author, ReadCover(zip, opf, opfPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException or NotSupportedException
                                       or ArgumentException or InvalidOperationException)
        {
            return EpubInfo.Empty;
        }
    }

    private static EpubCover? ReadCover(ZipArchive zip, XDocument opf, string opfPath)
    {
        var items = opf.Descendants(OpfNs + "manifest").Elements(OpfNs + "item")
            .Select(i => new
            {
                Id = (string?)i.Attribute("id"),
                Href = (string?)i.Attribute("href"),
                MediaType = ((string?)i.Attribute("media-type"))?.Trim().ToLowerInvariant(),
                Properties = ((string?)i.Attribute("properties"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Href))
            .ToList();

        // EPUB 3: properties="cover-image"; EPUB 2: <meta name="cover" content="item id">;
        // otherwise an image whose id or file name says "cover"
        var coverId = opf.Descendants(OpfNs + "meta")
            .Where(m => string.Equals((string?)m.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(m => (string?)m.Attribute("content"))
            .FirstOrDefault();

        var cover = items.FirstOrDefault(i => i.Properties.Contains("cover-image"))
                    ?? items.FirstOrDefault(i => coverId != null && i.Id == coverId)
                    ?? items.FirstOrDefault(i => i.MediaType?.StartsWith("image/") == true
                                                 && ((i.Id?.Contains("cover", StringComparison.OrdinalIgnoreCase) ?? false)
                                                     || i.Href!.Contains("cover", StringComparison.OrdinalIgnoreCase)));
        if (cover == null)
        {
            return null;
        }

        var path = Resolve(opfPath, cover.Href!);
        var bytes = path == null ? null : ReadEntry(zip, path, MaxCoverBytes);
        if (bytes == null)
        {
            return null;
        }

        // The type comes from the bytes, never from the book: SVG and anything else are left out
        var type = QuestionImageFiles.DetectType(bytes);
        return type is { } t ? new EpubCover(bytes, t.ContentType, t.Extension) : null;
    }

    /// <summary>
    /// The path inside the ZIP of a link relative to the package document ("../Images/c.jpg" from
    /// "OEBPS/content.opf" → "Images/c.jpg"). A link that leaves the archive or is absolute gives null.
    /// </summary>
    public static string? Resolve(string opfPath, string href)
    {
        var link = Uri.UnescapeDataString(href.Split('#', '?')[0]).Replace('\\', '/');
        if (link.Length == 0 || link.StartsWith('/') || link.Contains(':'))
        {
            return null;
        }

        var slash = opfPath.LastIndexOf('/');
        var parts = (slash < 0 ? new List<string>() : opfPath[..slash].Split('/').ToList());
        foreach (var part in link.Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    private static XDocument? LoadXml(ZipArchive zip, string path)
    {
        var bytes = ReadEntry(zip, path, MaxXmlBytes);
        if (bytes == null)
        {
            return null;
        }

        // No DTDs and no outside resources (XXE)
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
        return XDocument.Load(reader);
    }

    // Reads at most maxBytes; the size written in the ZIP is not trusted, the reading itself stops at the limit
    private static byte[]? ReadEntry(ZipArchive zip, string path, int maxBytes)
    {
        var entry = zip.GetEntry(path)
                    ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Length > maxBytes)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? Clean(string? text)
    {
        var line = ReaderText.OneLine(text, MaxTextLength);
        return line.Length == 0 ? null : line;
    }
}
