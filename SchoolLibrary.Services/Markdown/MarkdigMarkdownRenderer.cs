using Ganss.Xss;
using Markdig;

namespace SchoolLibrary.Services.Markdown;

/// <summary>
/// Renders Markdown with Markdig and then sanitizes the result. Raw HTML in the source is disabled
/// (rendered as text), and the sanitizer is a second line of defence that also drops unsafe link
/// schemes such as "javascript:".
/// </summary>
public class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;
    private readonly HtmlSanitizer _sanitizer;

    public MarkdigMarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        // The defaults allow common formatting tags and only the http(s) and mailto schemes
        _sanitizer = new HtmlSanitizer();
    }

    public string RenderToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
        return _sanitizer.Sanitize(html);
    }
}
