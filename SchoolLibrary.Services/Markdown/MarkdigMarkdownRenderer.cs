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
            .UseYamlFrontMatter()  // a "---" metadata header (e.g. source) is parsed, not shown as text
            .DisableHtml()
            .Build();

        // The defaults allow common formatting tags and only the http(s) and mailto schemes
        _sanitizer = new HtmlSanitizer();

        // Allow embedded images (data:image/...), so a self-contained Markdown file shows its pictures.
        // "data" is added to the allowed schemes, then narrowed below to image payloads only: a
        // data:text/html URL (a phishing/XSS vector in a link) is still dropped.
        _sanitizer.AllowedSchemes.Add("data");
        _sanitizer.FilterUrl += (_, e) =>
        {
            if (e.OriginalUrl is { } url
                && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                e.SanitizedUrl = null;
            }
        };
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
