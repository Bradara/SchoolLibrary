using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// Where a user stopped reading an uploaded book, so the reader continues from there on any device.
    /// One row per user and book; only the most recently read books of a user are kept.
    /// </summary>
    public class ReadingPosition
    {
        /// <summary>The longest EPUB location (CFI) that is stored; real ones are far shorter.</summary>
        public const int CfiMaxLength = 1000;

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        /// <summary>The EPUB location of the first visible text, for example "epubcfi(/6/4!/4/2/1:0)".</summary>
        [Required]
        [StringLength(CfiMaxLength, ErrorMessage = "Позицията в книгата е твърде дълга.")]
        public string Cfi { get; set; } = string.Empty;

        /// <summary>How far into the book the position is (0–100), shown without opening the book.</summary>
        [Range(0, 100, ErrorMessage = "Напредъкът трябва да е между 0 и 100%.")]
        public int Percent { get; set; }

        /// <summary>When the position was last saved (UTC); the oldest books are forgotten first.</summary>
        public DateTime UpdatedOn { get; set; }
    }
}
