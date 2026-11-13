using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>A comment under a resource. Comments cannot be edited, only deleted.</summary>
    public class Comment
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Напишете коментар")]
        [MaxLength(500, ErrorMessage = "Коментарът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Коментар")]
        public string Text { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        public string AuthorId { get; set; } = string.Empty;
        public ApplicationUser Author { get; set; } = null!;
    }
}
