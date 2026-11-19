using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A teacher's section (topic) in the question bank. Questions live in a section and can be reused
    /// across quizzes, either picked directly or drawn at random from the section.
    /// </summary>
    public class Section
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете име на раздела")]
        [MaxLength(200, ErrorMessage = "Името не може да бъде по-дълго от 200 символа")]
        [Display(Name = "Име")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
        [Display(Name = "Описание")]
        public string? Description { get; set; }

        public DateTime CreatedOn { get; set; }

        /// <summary>The parent section, for a tree of sections; null for a top-level (root) section.</summary>
        public int? ParentId { get; set; }
        public Section? Parent { get; set; }

        /// <summary>The teacher who owns the section; null if the account was deleted.</summary>
        public string? OwnerId { get; set; }
        public ApplicationUser? Owner { get; set; }

        public ICollection<Section> Children { get; set; } = new List<Section>();

        public ICollection<Question> Questions { get; set; } = new List<Question>();
    }
}
