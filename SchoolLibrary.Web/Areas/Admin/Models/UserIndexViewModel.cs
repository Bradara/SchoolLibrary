using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Areas.Admin.Models;

public class UserIndexViewModel
{
    public string? Search { get; set; }
    public bool Pending { get; set; }
    public IReadOnlyList<UserListItem> Users { get; set; } = new List<UserListItem>();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;
}
