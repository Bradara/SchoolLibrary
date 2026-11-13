using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A report about a resource or a comment. The links to the reported content, to the comment and to the
    /// reporter become null when those are deleted, so the report stays; the texts below keep what was reported.
    /// </summary>
    public class Report
    {
        public int Id { get; set; }

        public ReportTarget Target { get; set; }

        // For a comment report this is the resource the comment is under
        public int? ResourceId { get; set; }
        public Resource? Resource { get; set; }

        // Only for a comment report
        public int? CommentId { get; set; }
        public Comment? Comment { get; set; }

        /// <summary>Title of the resource at the time of the report.</summary>
        [MaxLength(100)]
        public string TargetTitle { get; set; } = string.Empty;

        /// <summary>Text of the comment at the time of the report; null for a resource report.</summary>
        [MaxLength(500)]
        public string? TargetText { get; set; }

        public ReportReason Reason { get; set; }

        [MaxLength(500, ErrorMessage = "Описанието не може да бъде по-дълго от 500 символа")]
        [Display(Name = "Описание")]
        public string? Details { get; set; }

        public ReportStatus Status { get; set; } = ReportStatus.New;

        public DateTime CreatedOn { get; set; }

        public DateTime? ReviewedOn { get; set; }

        // Null when the reporter deleted their account
        public string? ReporterId { get; set; }
        public ApplicationUser? Reporter { get; set; }
    }
}
