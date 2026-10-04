using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace SchoolLibrary.Data.Models
{
    public class Resource
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Заглавието е задължително")]
        [MaxLength(100, ErrorMessage = "Заглавието не може да бъде по-дълго от 100 символа")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "URL адресът е задължителен")]
        [MaxLength(500, ErrorMessage = "URL адресът не може да бъде по-дълъг от 500 символа")]
        public string Url { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        public int CategoryId { get; set; }
        public Category? Category { get; set; }

        public int GradeLevelId { get; set; }
        public GradeLevel? GradeLevel { get; set; }

        public string OwnerId { get; set; } = string.Empty;
        public IdentityUser? Owner { get; set; }
    }
}
