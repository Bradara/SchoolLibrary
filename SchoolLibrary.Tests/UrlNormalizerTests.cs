using SchoolLibrary.Data.Common;

namespace SchoolLibrary.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("http://example.com/page", "https://example.com/page")]
    [InlineData("https://www.example.com/page", "https://example.com/page")]
    [InlineData("HTTPS://Example.COM/page", "https://example.com/page")]
    [InlineData("https://example.com/page/", "https://example.com/page")]
    [InlineData("https://example.com/page#section", "https://example.com/page")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("https://example.com/", "https://example.com")]
    [InlineData("  https://example.com/page  ", "https://example.com/page")]
    public void Normalize_GivesOneCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_KeepsPathCase()
    {
        Assert.Equal("https://example.com/Files/Lesson.PDF", UrlNormalizer.Normalize("https://example.com/Files/Lesson.PDF"));
    }

    [Fact]
    public void Normalize_KeepsNonDefaultPort()
    {
        Assert.Equal("https://example.com:8443/a", UrlNormalizer.Normalize("https://example.com:8443/a"));
    }

    [Theory]
    [InlineData("https://example.com/a?utm_source=x&utm_medium=y", "https://example.com/a")]
    [InlineData("https://example.com/a?id=5&fbclid=abc", "https://example.com/a?id=5")]
    [InlineData("https://example.com/a?b=2&a=1", "https://example.com/a?a=1&b=2")]
    [InlineData("https://example.com/a?a=1&b=2", "https://example.com/a?a=1&b=2")]
    public void Normalize_DropsTrackingParametersAndSortsTheRest(string input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=30s")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    public void Normalize_YouTubeVariants_AreTheSameVideo(string input)
    {
        Assert.Equal("https://youtube.com/watch?v=dQw4w9WgXcQ", UrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_YouTubeDifferentVideos_AreDifferent()
    {
        Assert.NotEqual(
            UrlNormalizer.Normalize("https://youtu.be/aaaaaaaaaaa"),
            UrlNormalizer.Normalize("https://youtu.be/bbbbbbbbbbb"));
    }

    [Fact]
    public void Normalize_YouTubeLinkWithoutVideo_FallsBackToGenericForm()
    {
        Assert.Equal("https://youtube.com/@channel", UrlNormalizer.Normalize("https://www.youtube.com/@channel/"));
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/abc123_-X/view?usp=sharing")]
    [InlineData("https://drive.google.com/file/d/abc123_-X/preview")]
    [InlineData("https://drive.google.com/open?id=abc123_-X")]
    [InlineData("https://drive.google.com/uc?id=abc123_-X&export=download")]
    public void Normalize_GoogleDriveVariants_AreTheSameFile(string input)
    {
        Assert.Equal("https://drive.google.com/file/d/abc123_-X", UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("https://docs.google.com/document/d/DOC1/edit?usp=sharing", "https://docs.google.com/document/d/DOC1")]
    [InlineData("https://docs.google.com/document/d/DOC1/preview", "https://docs.google.com/document/d/DOC1")]
    [InlineData("https://docs.google.com/presentation/d/SL1/embed?start=false", "https://docs.google.com/presentation/d/SL1")]
    [InlineData("https://docs.google.com/spreadsheets/d/SH1/edit#gid=0", "https://docs.google.com/spreadsheets/d/SH1")]
    public void Normalize_GoogleDocsVariants_AreTheSameDocument(string input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    public void Normalize_NotAnHttpLink_ReturnsNull(string? input)
    {
        Assert.Null(UrlNormalizer.Normalize(input));
    }
}
