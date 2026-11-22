using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>A place in an uploaded book that a user marked to come back to. Seen only by that user.</summary>
    public class Bookmark
    {
        public const int LabelMaxLength = 200;

        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        /// <summary>The EPUB location of the marked page.</summary>
        [Required]
        [StringLength(ReadingPosition.CfiMaxLength, ErrorMessage = "Позицията в книгата е твърде дълга.")]
        public string Cfi { get; set; } = string.Empty;

        /// <summary>What the list shows: the chapter and the first words of the page.</summary>
        [Required]
        [StringLength(LabelMaxLength, ErrorMessage = "Описанието на отметката е твърде дълго.")]
        public string Label { get; set; } = string.Empty;

        /// <summary>How far into the book the bookmark is (0–100); the list is ordered by it.</summary>
        [Range(0, 100, ErrorMessage = "Напредъкът трябва да е между 0 и 100%.")]
        public int Percent { get; set; }

        public DateTime CreatedOn { get; set; }
    }
}
