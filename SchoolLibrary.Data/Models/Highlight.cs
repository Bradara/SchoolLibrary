using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A passage a user highlighted in an uploaded book, with an optional note. Seen only by that user.
    /// </summary>
    public class Highlight
    {
        public const int TextMaxLength = 1000;
        public const int NoteMaxLength = 2000;
        public const int ColorMaxLength = 10;

        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        /// <summary>The EPUB range of the passage, for example "epubcfi(/6/4!/4/2,/1:0,/1:25)".</summary>
        [Required]
        [StringLength(ReadingPosition.CfiMaxLength, ErrorMessage = "Позицията в книгата е твърде дълга.")]
        public string Cfi { get; set; } = string.Empty;

        /// <summary>A copy of the highlighted text, for the list of notes (cut to the column length).</summary>
        [Required]
        [StringLength(TextMaxLength, ErrorMessage = "Подчертаният текст е твърде дълъг.")]
        public string Text { get; set; } = string.Empty;

        /// <summary>One of the reader's colors (yellow, green, blue, pink).</summary>
        [Required]
        [StringLength(ColorMaxLength)]
        public string Color { get; set; } = string.Empty;

        [StringLength(NoteMaxLength, ErrorMessage = "Бележката може да е до {1} знака.")]
        public string? Note { get; set; }

        /// <summary>How far into the book the passage is (0–100); the list is ordered by it.</summary>
        [Range(0, 100, ErrorMessage = "Напредъкът трябва да е между 0 и 100%.")]
        public int Percent { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime UpdatedOn { get; set; }
    }
}
