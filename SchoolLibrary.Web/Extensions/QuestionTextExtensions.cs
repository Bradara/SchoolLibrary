using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Extensions;

/// <summary>
/// Writes a question text with its pictures in place of the <c>[[img:N]]</c> markers. The text parts are
/// HTML-encoded (the text never holds HTML) and nothing else is added, so the result can sit inside a
/// heading or a <c>white-space: pre-line</c> block without stray line breaks.
/// </summary>
public static class QuestionTextExtensions
{
    public static IHtmlContent QuestionText(this IHtmlHelper html, Question question, bool compact = false)
        => html.QuestionText(QuestionTextModel.ForText(question, compact));

    public static IHtmlContent QuestionStatement(this IHtmlHelper html, Question question, bool compact = false)
        => html.QuestionText(QuestionTextModel.ForStatement(question, compact));

    public static IHtmlContent QuestionText(this IHtmlHelper html, QuestionTextModel model)
    {
        var url = html.ViewContext.HttpContext.RequestServices
            .GetRequiredService<IUrlHelperFactory>()
            .GetUrlHelper(html.ViewContext);
        var css = model.Compact ? "question-image is-compact" : "question-image";

        var content = new HtmlContentBuilder();
        foreach (var part in model.Parts)
        {
            if (part.Image == null)
            {
                content.Append(part.Text);
            }
            else
            {
                AppendImage(content, url, part.Image, css);
            }
        }

        foreach (var image in model.After)
        {
            AppendImage(content, url, image, css);
        }

        return content;
    }

    private static void AppendImage(HtmlContentBuilder content, IUrlHelper url, QuestionImage image, string css)
    {
        if (QuestionImageFiles.FileIdOf(image.StorageKey) is not { } fileId)
        {
            return;
        }

        var tag = new TagBuilder("img") { TagRenderMode = TagRenderMode.SelfClosing };
        tag.Attributes["src"] = url.Action("Image", "Quizzes", new { area = "", id = fileId.ToString("N") });
        tag.Attributes["alt"] = image.AltText ?? $"Изображение {image.Number}";
        tag.Attributes["class"] = css;
        tag.Attributes["loading"] = "lazy";
        content.AppendHtml(tag);
    }
}
