using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests;

public class ResourceEmbedTests
{
    private static Resource WithUrl(string url) => new() { Url = url };

    [Theory]
    [InlineData("https://bg.wikisource.org/wiki/Под_игото")]
    [InlineData("https://ru.wikisource.org/wiki/Сказка_о_царе_Салтане_(Пушкин)")]
    [InlineData("https://wikisource.org/wiki/Main_Page")]
    [InlineData("https://en.wikibooks.org/wiki/Algebra")]
    [InlineData("https://www.gutenberg.org/ebooks/11")]
    [InlineData("https://gutenberg.org/cache/epub/11/pg11-images.html")]
    [InlineData("https://blockly.games/turtle?lang=bg")]
    [InlineData("https://jspaint.app/")]
    public void TrustedDomains_AreEmbedded(string url)
    {
        var resource = WithUrl(url);

        Assert.True(resource.IsEmbeddable);
        Assert.Equal(url, resource.EmbedUrl);
    }

    [Fact]
    public void TrustedDomain_OverHttp_IsEmbeddedOverHttps()
    {
        var resource = WithUrl("http://bg.wikisource.org/wiki/Under");

        Assert.Equal("https://bg.wikisource.org/wiki/Under", resource.EmbedUrl);
    }

    [Theory]
    [InlineData("https://evilwikisource.org/wiki/Page")]
    [InlineData("https://wikisource.org.evil.com/wiki/Page")]
    [InlineData("https://evil.com/wikisource.org")]
    [InlineData("https://notgutenberg.org/ebooks/11")]
    [InlineData("https://evilblockly.games/turtle")]
    [InlineData("https://jspaint.app.evil.com/")]
    [InlineData("https://en.wikipedia.org/wiki/Bulgaria")]
    [InlineData("https://example.com/page")]
    public void OtherDomains_AreNotEmbedded(string url)
    {
        Assert.False(WithUrl(url).IsEmbeddable);
    }

    [Theory]
    [InlineData("https://example.com/book.pdf", "https://example.com/book.pdf")]
    [InlineData("https://example.com/page.html", "https://example.com/page.html")]
    [InlineData("https://user.github.io/site", "https://user.github.io/site")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://drive.google.com/file/d/abc/view?usp=sharing", "https://drive.google.com/file/d/abc/preview")]
    [InlineData("https://docs.google.com/presentation/d/abc/edit", "https://docs.google.com/presentation/d/abc/embed")]
    public void ExistingRules_StillWork(string url, string expected)
    {
        Assert.Equal(expected, WithUrl(url).EmbedUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public void InvalidLink_IsNotEmbedded(string url)
    {
        Assert.False(WithUrl(url).IsEmbeddable);
    }
}
