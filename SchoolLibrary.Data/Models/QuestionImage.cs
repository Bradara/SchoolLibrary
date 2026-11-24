using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A picture shown in the text of a question. The text refers to it with the marker
    /// <c>[[img:Number]]</c>; an image whose marker is missing is shown below the text. Only the metadata
    /// lives in the database; the bytes are kept by the file storage under <see cref="StorageKey"/>.
    /// Copies of a question may share the same stored file, so the file is removed only when no image
    /// row refers to its key any more.
    /// </summary>
    public class QuestionImage
    {
        public const int MaxPerQuestion = 10;
        public const int MaxSizeBytes = 2 * 1024 * 1024;
        public const int StorageKeyMaxLength = 200;
        public const int FileNameMaxLength = 255;
        public const int ContentTypeMaxLength = 50;
        public const int AltTextMaxLength = 200;

        public int Id { get; set; }

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;

        /// <summary>The number used in the marker <c>[[img:Number]]</c>, unique within the question (1–10).</summary>
        [Range(1, MaxPerQuestion)]
        public int Number { get; set; }

        /// <summary>The key the file is stored under, e.g. "questions/{guid}.png".</summary>
        [Required]
        [MaxLength(StorageKeyMaxLength)]
        public string StorageKey { get; set; } = string.Empty;

        /// <summary>The original file name (kept for the Moodle export).</summary>
        [Required]
        [MaxLength(FileNameMaxLength)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>image/png, image/jpeg, image/gif or image/webp, detected from the bytes.</summary>
        [Required]
        [MaxLength(ContentTypeMaxLength)]
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        /// <summary>A short description for screen readers; empty for a decorative picture.</summary>
        [MaxLength(AltTextMaxLength, ErrorMessage = "Описанието на изображението не може да бъде по-дълго от 200 символа")]
        public string? AltText { get; set; }
    }
}
