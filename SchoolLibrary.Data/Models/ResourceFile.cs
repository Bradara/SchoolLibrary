using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A file uploaded for a resource (<see cref="Enums.ResourceKind.Upload"/>). Only the metadata lives
    /// in the database; the bytes are kept by the file storage under <see cref="StorageKey"/>.
    /// </summary>
    public class ResourceFile
    {
        public const int StorageKeyMaxLength = 200;
        public const int FileNameMaxLength = 255;
        public const int ContentTypeMaxLength = 100;
        public const int Sha256Length = 64;

        public int Id { get; set; }

        // The resource this file belongs to; one file per resource (unique index)
        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        /// <summary>The key the file is stored under, e.g. "resources/{guid}.pdf".</summary>
        [Required]
        [MaxLength(StorageKeyMaxLength)]
        public string StorageKey { get; set; } = string.Empty;

        /// <summary>The original file name, shown to the user and used when downloading.</summary>
        [Required]
        [MaxLength(FileNameMaxLength)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>The MIME type the file is served with (application/pdf, application/epub+zip, text/markdown).</summary>
        [Required]
        [MaxLength(ContentTypeMaxLength)]
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        /// <summary>SHA-256 of the content as lowercase hex, used to detect a file already in the library.</summary>
        [Required]
        [MaxLength(Sha256Length)]
        public string Sha256 { get; set; } = string.Empty;

        public DateTime UploadedOn { get; set; }

        /// <summary>
        /// The key of the cover picture taken out of an EPUB (e.g. "covers/{guid}.jpg"), or null when the book
        /// has none or the file is not a book.
        /// </summary>
        [MaxLength(StorageKeyMaxLength)]
        public string? CoverKey { get; set; }

        /// <summary>The type of the cover (PNG, JPEG, GIF or WebP, recognized from the bytes); null without a cover.</summary>
        [MaxLength(ContentTypeMaxLength)]
        public string? CoverContentType { get; set; }
    }
}
