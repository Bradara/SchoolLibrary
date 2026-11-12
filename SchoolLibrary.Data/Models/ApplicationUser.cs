using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolLibrary.Data.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True while a teacher registration waits for an administrator. The user has
        /// student rights until the request is approved.
        /// </summary>
        public bool TeacherRequested { get; set; }

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";
    }
}
