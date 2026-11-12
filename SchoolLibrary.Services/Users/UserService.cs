using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Users;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;

    public UserService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<UserListItem>> SearchAsync(UserFilter filter)
    {
        var query = _context.Users.AsNoTracking();

        if (filter.OnlyPendingTeachers)
        {
            query = query.Where(u => u.TeacherRequested);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var text = filter.SearchText.Trim().ToLower();
            query = query.Where(u =>
                (u.FirstName + " " + u.LastName).ToLower().Contains(text) ||
                (u.Email != null && u.Email.ToLower().Contains(text)));
        }

        var pageSize = Math.Clamp(filter.PageSize, 1, UserFilter.MaxPageSize);
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, totalPages);

        var users = await query
            .OrderBy(u => u.CreatedOn).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = new List<UserListItem>();
        foreach (var user in users)
        {
            items.Add(await ToItemAsync(user));
        }

        return new PagedResult<UserListItem>(items, total, page, pageSize);
    }

    public async Task<ServiceResult<UserListItem>> ApproveTeacherAsync(string userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.NotFound, "Потребителят не е намерен.");
        }

        if (!user.TeacherRequested)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Invalid, "Потребителят не е подал заявка за учител.");
        }

        var teacherRoleId = await GetRoleIdAsync(Roles.Teacher);
        if (teacherRoleId == null)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Invalid, "Ролята „Учител“ не съществува.");
        }

        await AddToRoleAsync(user.Id, teacherRoleId);
        await RemoveFromRoleAsync(user.Id, Roles.Student);
        user.TeacherRequested = false;
        await _context.SaveChangesAsync();

        return ServiceResult<UserListItem>.Success(await ToItemAsync(user));
    }

    public async Task<ServiceResult<UserListItem>> RejectTeacherAsync(string userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.NotFound, "Потребителят не е намерен.");
        }

        if (!user.TeacherRequested)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Invalid, "Потребителят не е подал заявка за учител.");
        }

        user.TeacherRequested = false;
        await _context.SaveChangesAsync();

        return ServiceResult<UserListItem>.Success(await ToItemAsync(user));
    }

    public async Task<ServiceResult<UserListItem>> SetRoleAsync(string userId, string role, bool enabled, string currentUserId)
    {
        if (!Roles.Assignable.Contains(role))
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Invalid, "Ролята не може да се променя.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.NotFound, "Потребителят не е намерен.");
        }

        if (!enabled && role == Roles.Administrator && userId == currentUserId)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Forbidden, "Не можете да премахнете собствената си администраторска роля.");
        }

        var roleId = await GetRoleIdAsync(role);
        if (roleId == null)
        {
            return ServiceResult<UserListItem>.Failure(ServiceError.Invalid, "Ролята не съществува.");
        }

        if (enabled)
        {
            await AddToRoleAsync(user.Id, roleId);
            if (role == Roles.Teacher)
            {
                user.TeacherRequested = false;
            }
        }
        else
        {
            await RemoveFromRoleAsync(user.Id, role);
        }

        await _context.SaveChangesAsync();
        return ServiceResult<UserListItem>.Success(await ToItemAsync(user));
    }

    private Task<string?> GetRoleIdAsync(string roleName) =>
        _context.Roles.Where(r => r.Name == roleName).Select(r => r.Id).FirstOrDefaultAsync();

    private async Task AddToRoleAsync(string userId, string roleId)
    {
        var exists = await _context.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (!exists)
        {
            _context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        }
    }

    private async Task RemoveFromRoleAsync(string userId, string roleName)
    {
        var links = await (from ur in _context.UserRoles
                           join r in _context.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId && r.Name == roleName
                           select ur).ToListAsync();
        _context.UserRoles.RemoveRange(links);
    }

    private async Task<UserListItem> ToItemAsync(ApplicationUser user)
    {
        var roles = await (from ur in _context.UserRoles
                           join r in _context.Roles on ur.RoleId equals r.Id
                           where ur.UserId == user.Id
                           orderby r.Name
                           select r.Name!).ToListAsync();

        return new UserListItem(user.Id, user.FullName, user.Email ?? string.Empty, user.CreatedOn, user.TeacherRequested, roles);
    }
}
