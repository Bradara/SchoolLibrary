using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.ViewModels;

/// <summary>The form for reporting a resource or a comment. Exactly one of the ids is set.</summary>
public class ReportFormViewModel
{
    public int? ResourceId { get; set; }

    public int? CommentId { get; set; }

    [Required(ErrorMessage = "Изберете причина")]
    [Display(Name = "Причина")]
    public ReportReason? Reason { get; set; }

    [MaxLength(500, ErrorMessage = "Описанието не може да бъде по-дълго от 500 символа")]
    [Display(Name = "Описание")]
    public string? Details { get; set; }

    // What is being reported, filled by the controller (not posted back)
    public string TargetTitle { get; set; } = string.Empty;

    /// <summary>The comment text, when a comment is reported.</summary>
    public string? TargetText { get; set; }

    public string? TargetAuthor { get; set; }

    /// <summary>The resource page to go back to (the resource itself, or the one a comment is under).</summary>
    public int BackResourceId { get; set; }

    public bool IsComment => CommentId.HasValue;
}
