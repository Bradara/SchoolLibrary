using SchoolLibrary.Services.Markdown;

namespace SchoolLibrary.Tests.Services;

public class MarkdigMarkdownRendererTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RenderToHtml_BlankInput_ReturnsEmpty(string? markdown)
    {
        Assert.Equal(string.Empty, _renderer.RenderToHtml(markdown!));
    }

    [Fact]
    public void RenderToHtml_Heading_BecomesHeadingTag()
    {
        var html = _renderer.RenderToHtml("# Заглавие");

        Assert.Contains("<h1", html);
        Assert.Contains("Заглавие", html);
    }

    [Fact]
    public void RenderToHtml_Emphasis_BecomesStrong()
    {
        Assert.Contains("<strong>важно</strong>", _renderer.RenderToHtml("Това е **важно**."));
    }

    [Fact]
    public void RenderToHtml_SafeLink_IsKept()
    {
        var html = _renderer.RenderToHtml("[сайт](https://example.com)");

        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public void RenderToHtml_RawScript_IsNotEmittedAsHtml()
    {
        var html = _renderer.RenderToHtml("Текст <script>alert('xss')</script> още текст");

        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void RenderToHtml_JavascriptLink_IsStripped()
    {
        var html = _renderer.RenderToHtml("[натисни](javascript:alert('xss'))");

        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void RenderToHtml_RawImgWithOnError_IsNotEmittedAsHtml()
    {
        // Raw HTML is disabled, so the tag is escaped to text and never becomes a real <img onerror=...>
        var html = _renderer.RenderToHtml("<img src=x onerror=alert('xss')>");

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void RenderToHtml_ExternalImage_IsKept()
    {
        var html = _renderer.RenderToHtml("![схема](https://example.com/diagram.png)");

        Assert.Contains("src=\"https://example.com/diagram.png\"", html);
    }

    [Fact]
    public void RenderToHtml_EmbeddedDataImage_IsKept()
    {
        // 1x1 transparent PNG as a data URI, the way a self-contained export embeds pictures
        const string dataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var html = _renderer.RenderToHtml($"![точка]({dataUri})");

        Assert.Contains("data:image/png;base64", html);
    }

    [Fact]
    public void RenderToHtml_NonImageDataUri_IsStripped()
    {
        // data:text/html is a phishing/XSS vector and must not survive, even written as an image
        var html = _renderer.RenderToHtml("![x](data:text/html,<h1>hi</h1>)");

        Assert.DoesNotContain("data:text/html", html);
    }

    [Fact]
    public void RenderToHtml_YamlFrontMatter_IsNotShownAsText()
    {
        var markdown = "---\nsource: https://example.com/page\ntitle: Урок\n---\n# Съдържание";

        var html = _renderer.RenderToHtml(markdown);

        Assert.Contains("<h1", html);
        Assert.Contains("Съдържание", html);
        Assert.DoesNotContain("source:", html);
    }
}
