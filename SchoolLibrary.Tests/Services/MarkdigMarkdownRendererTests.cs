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
}
