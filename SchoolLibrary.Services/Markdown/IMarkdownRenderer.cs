namespace SchoolLibrary.Services.Markdown;

/// <summary>Turns Markdown text into HTML that is safe to place on a page.</summary>
public interface IMarkdownRenderer
{
    /// <summary>Renders Markdown to sanitized HTML (no scripts, no unsafe links or raw HTML).</summary>
    string RenderToHtml(string markdown);
}
