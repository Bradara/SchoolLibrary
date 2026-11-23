using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Data.Common;

/// <summary>What a resource is to the reader. Worked out from the link or the uploaded file, not stored.</summary>
public enum ResourceMediaType
{
    /// <summary>A web page, and anything that is not recognized.</summary>
    Page,
    Video,

    /// <summary>A simulation, an applet or a programming environment.</summary>
    Interactive,
    Pdf,
    Epub,
    Markdown,

    /// <summary>A Google document, presentation or spreadsheet, or an uploaded file of an unknown type.</summary>
    Document
}

/// <summary>How a media type is shown: a Bootstrap icon class and a label for the interface.</summary>
public record MediaInfo(ResourceMediaType Type, string Icon, string Label);

/// <summary>Recognizes the media type of a resource, for the icon and the label on the catalog cards.</summary>
public static class ResourceMedia
{
    private static readonly Dictionary<ResourceMediaType, MediaInfo> Infos = new[]
    {
        new MediaInfo(ResourceMediaType.Page, "bi-globe2", "Страница"),
        new MediaInfo(ResourceMediaType.Video, "bi-play-btn", "Видео"),
        new MediaInfo(ResourceMediaType.Interactive, "bi-joystick", "Интерактивно"),
        new MediaInfo(ResourceMediaType.Pdf, "bi-file-earmark-pdf", "PDF"),
        new MediaInfo(ResourceMediaType.Epub, "bi-book-half", "Е-книга"),
        new MediaInfo(ResourceMediaType.Markdown, "bi-markdown", "Текст"),
        new MediaInfo(ResourceMediaType.Document, "bi-file-earmark-text", "Документ")
    }.ToDictionary(i => i.Type);

    // The site itself or any of its subdomains (www., player., bg.)
    private static readonly string[] VideoHosts = { "youtube.com", "youtu.be", "youtube-nocookie.com", "vimeo.com" };

    private static readonly string[] InteractiveHosts =
    {
        "phet.colorado.edu",
        "geogebra.org",
        "scratch.mit.edu",
        "blockly.games",
        "jspaint.app",
        "learningapps.org",
        "wordwall.net",
        "desmos.com",
        "code.org"
    };

    private static readonly string[] DocumentHosts = { "docs.google.com", "drive.google.com" };

    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".ogv" };

    public static IReadOnlyCollection<MediaInfo> All => Infos.Values;

    public static MediaInfo Info(ResourceMediaType type) => Infos[type];

    /// <summary>The media type together with its icon and label.</summary>
    public static MediaInfo For(Resource resource) => Info(Detect(resource));

    public static ResourceMediaType Detect(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // An uploaded resource has no link: its type is the type the upload check gave the file
        if (resource.Kind == ResourceKind.Upload)
        {
            return FromContentType(resource.File?.ContentType);
        }

        return FromUrl(resource.Url);
    }

    private static ResourceMediaType FromContentType(string? contentType)
    {
        // "text/markdown; charset=utf-8": only the type before the parameters counts
        var type = contentType?.Split(';')[0].Trim().ToLowerInvariant();

        return type switch
        {
            "application/pdf" => ResourceMediaType.Pdf,
            "application/epub+zip" => ResourceMediaType.Epub,
            "text/markdown" => ResourceMediaType.Markdown,
            _ => ResourceMediaType.Document
        };
    }

    private static ResourceMediaType FromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ResourceMediaType.Page;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        if (IsOneOf(host, VideoHosts) || VideoExtensions.Any(path.EndsWith))
        {
            return ResourceMediaType.Video;
        }

        if (IsOneOf(host, InteractiveHosts))
        {
            return ResourceMediaType.Interactive;
        }

        if (IsOneOf(host, DocumentHosts))
        {
            return ResourceMediaType.Document;
        }

        if (path.EndsWith(".pdf")) return ResourceMediaType.Pdf;
        if (path.EndsWith(".epub")) return ResourceMediaType.Epub;
        if (path.EndsWith(".md") || path.EndsWith(".markdown")) return ResourceMediaType.Markdown;

        return ResourceMediaType.Page;
    }

    // The domain itself or a subdomain of it, but not "evilyoutube.com" or "youtube.com.evil.com"
    private static bool IsOneOf(string host, string[] domains) =>
        domains.Any(d => host == d || host.EndsWith("." + d));
}
