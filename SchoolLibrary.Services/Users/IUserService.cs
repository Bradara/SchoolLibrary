using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Users;

public record UserListItem(
    string Id,
    string FullName,
    string Email,
    DateTime CreatedOn,
    bool TeacherRequested,
    IReadOnlyList<string> Roles);

public class UserFilter
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public string? SearchText { get; set; }
    public bool OnlyPendingTeachers { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
}

public interface IUserService
{
    Task<PagedResult<UserListItem>> SearchAsync(UserFilter filter);

    /// <summary>Gives the user the Teacher role (instead of Student) and clears the request.</summary>
    Task<ServiceResult<UserListItem>> ApproveTeacherAsync(string userId);

    /// <summary>Clears the teacher request; the user stays a student.</summary>
    Task<ServiceResult<UserListItem>> RejectTeacherAsync(string userId);

    /// <summary>
    /// Adds or removes one of the assignable roles (Administrator, Teacher, Student, Parent).
    /// Nobody can remove the Administrator role from themselves. Giving the Teacher role clears a pending request.
    /// </summary>
    Task<ServiceResult<UserListItem>> SetRoleAsync(string userId, string role, bool enabled, string currentUserId);
}
