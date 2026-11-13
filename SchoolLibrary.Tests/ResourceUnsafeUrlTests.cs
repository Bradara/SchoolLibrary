using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests;

// Links are validated when a resource is saved, but the page must also be safe with data that got into the
// database some other way (older rows, a manual change): a "javascript:" address must never reach an
// iframe src or a link href, where it would run script on this site.
public class ResourceUnsafeUrlTests
{
    private static Resource WithUrl(string url) => new() { Url = url };

    [Theory]
    [InlineData("javascript:alert(1)//x.pdf")]
    [InlineData("javascript:alert(document.cookie)//page.html")]
    [InlineData("JaVaScRiPt:alert(1)//a.pdf")]
    [InlineData("data:text/html,<script>alert(1)</script>.html")]
    [InlineData("vbscript:msgbox(1)//a.htm")]
    [InlineData("file:///etc/passwd.pdf")]
    [InlineData("ftp://example.com/book.pdf")]
    public void NonHttpAddress_IsNeverEmbedded(string url)
    {
        var resource = WithUrl(url);

        Assert.False(resource.IsEmbeddable);
        Assert.Null(resource.EmbedUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("vbscript:x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("")]
    public void NonHttpAddress_HasNoSafeLink(string url)
    {
        Assert.Null(WithUrl(url).SafeUrl);
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com/a")]
    [InlineData("HTTPS://EXAMPLE.COM/a")]
    public void HttpAddress_IsASafeLink(string url)
    {
        Assert.Equal(url, WithUrl(url).SafeUrl);
    }
}
