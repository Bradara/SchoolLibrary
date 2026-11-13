namespace SchoolLibrary.Data.Models
{
    /// <summary>A resource a user saved in their favorites. One row per user and resource.</summary>
    public class Favorite
    {
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public int ResourceId { get; set; }
        public Resource Resource { get; set; } = null!;

        public DateTime CreatedOn { get; set; }
    }
}
