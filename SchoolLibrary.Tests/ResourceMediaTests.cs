using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests;

public class ResourceMediaTests
{
    private static ResourceMediaType Detect(string url, ResourceKind kind = ResourceKind.Lesson) =>
        ResourceMedia.Detect(new Resource { Url = url, Kind = kind });

    private static ResourceMediaType DetectUpload(string? contentType) =>
        ResourceMedia.Detect(new Resource
        {
            Kind = ResourceKind.Upload,
            Url = string.Empty,
            File = contentType == null ? null : new ResourceFile { ContentType = contentType }
        });

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/76979871")]
    [InlineData("https://player.vimeo.com/video/76979871")]
    [InlineData("https://example.com/media/lesson.MP4")]
    [InlineData("https://example.com/media/lesson.webm")]
    public void Videos_AreVideo(string url)
    {
        Assert.Equal(ResourceMediaType.Video, Detect(url));
    }

    [Theory]
    [InlineData("https://phet.colorado.edu/sims/html/density/latest/density_all.html")]
    [InlineData("https://www.geogebra.org/m/BYGyGD6E")]
    [InlineData("https://scratch.mit.edu/projects/123456")]
    [InlineData("https://blockly.games/turtle?lang=bg")]
    [InlineData("https://jspaint.app/")]
    [InlineData("https://learningapps.org/1234567")]
    [InlineData("https://wordwall.net/bg/resource/123")]
    [InlineData("https://www.desmos.com/calculator")]
    [InlineData("https://studio.code.org/s/course1")]
    public void Simulations_AndProgramming_AreInteractive(string url)
    {
        // The PhET page ends with ".html", but it is a simulation, not a page
        Assert.Equal(ResourceMediaType.Interactive, Detect(url));
    }

    [Theory]
    [InlineData("https://docs.google.com/document/d/abc/edit")]
    [InlineData("https://docs.google.com/presentation/d/abc/edit")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc/edit")]
    [InlineData("https://drive.google.com/file/d/abc/view")]
    public void GoogleFiles_AreDocuments(string url)
    {
        Assert.Equal(ResourceMediaType.Document, Detect(url));
    }

    [Theory]
    [InlineData("https://example.com/files/book.pdf", ResourceMediaType.Pdf)]
    [InlineData("https://example.com/files/BOOK.PDF?download=1", ResourceMediaType.Pdf)]
    [InlineData("https://example.com/files/book.epub", ResourceMediaType.Epub)]
    [InlineData("https://raw.githubusercontent.com/user/repo/main/README.md", ResourceMediaType.Markdown)]
    [InlineData("https://example.com/notes.markdown", ResourceMediaType.Markdown)]
    public void FileLinks_AreRecognizedByTheirExtension(string url, ResourceMediaType expected)
    {
        Assert.Equal(expected, Detect(url, ResourceKind.External));
    }

    [Theory]
    [InlineData("https://bg.wikipedia.org/wiki/България")]
    [InlineData("https://bg.wikisource.org/wiki/Под_игото")]
    [InlineData("https://user.github.io/lesson/index.html")]
    [InlineData("https://archive.org/details/something")]
    [InlineData("https://example.com/")]
    public void OtherLinks_ArePages(string url)
    {
        Assert.Equal(ResourceMediaType.Page, Detect(url));
    }

    [Theory]
    [InlineData("https://evilyoutube.com/watch?v=x")]
    [InlineData("https://youtube.com.evil.com/watch?v=x")]
    [InlineData("https://evil.com/youtube.com")]
    [InlineData("https://notphet.colorado.edu.evil.com/sim")]
    [InlineData("https://example.com/pdf")]
    [InlineData("https://example.com/book.pdf/page")]
    public void LookAlikeLinks_ArePages(string url)
    {
        Assert.Equal(ResourceMediaType.Page, Detect(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a link")]
    [InlineData("/relative/lesson.pdf")]
    [InlineData("javascript:alert(1)//a.mp4")]
    [InlineData("ftp://example.com/book.pdf")]
    public void MissingOrUnsafeLinks_ArePages(string? url)
    {
        Assert.Equal(ResourceMediaType.Page, Detect(url!));
    }

    [Theory]
    [InlineData("application/pdf", ResourceMediaType.Pdf)]
    [InlineData("application/epub+zip", ResourceMediaType.Epub)]
    [InlineData("text/markdown; charset=utf-8", ResourceMediaType.Markdown)]
    [InlineData("Text/Markdown", ResourceMediaType.Markdown)]
    [InlineData("application/zip", ResourceMediaType.Document)]
    [InlineData("", ResourceMediaType.Document)]
    [InlineData(null, ResourceMediaType.Document)]
    public void Uploads_AreRecognizedByTheTypeOfTheFile(string? contentType, ResourceMediaType expected)
    {
        Assert.Equal(expected, DetectUpload(contentType));
    }

    [Fact]
    public void Upload_IgnoresTheLink()
    {
        var resource = new Resource
        {
            Kind = ResourceKind.Upload,
            Url = "https://www.youtube.com/watch?v=x",
            File = new ResourceFile { ContentType = "application/pdf" }
        };

        Assert.Equal(ResourceMediaType.Pdf, ResourceMedia.Detect(resource));
    }

    [Fact]
    public void Detect_WithoutAResource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ResourceMedia.Detect(null!));
    }

    [Fact]
    public void For_GivesTheIconAndTheLabel()
    {
        var info = ResourceMedia.For(new Resource { Url = "https://youtu.be/x" });

        Assert.Equal(new MediaInfo(ResourceMediaType.Video, "bi-play-btn", "Видео"), info);
    }

    [Fact]
    public void EveryType_HasAnInfo_WithADistinctLabelAndIcon()
    {
        var types = Enum.GetValues<ResourceMediaType>();

        Assert.All(types, t => Assert.Equal(t, ResourceMedia.Info(t).Type));
        Assert.Equal(types.Length, ResourceMedia.All.Count);
        Assert.Equal(types.Length, ResourceMedia.All.Select(i => i.Label).Distinct().Count());
        Assert.Equal(types.Length, ResourceMedia.All.Select(i => i.Icon).Distinct().Count());
    }

    [Fact]
    public void EveryIcon_ExistsInBootstrapIcons()
    {
        var css = File.ReadAllText(TestFiles.BootstrapIconsCss());

        Assert.All(ResourceMedia.All, i => Assert.Contains("." + i.Icon + "::before", css));
    }
}
