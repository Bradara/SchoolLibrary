using SchoolLibrary.Data.Constants;

namespace SchoolLibrary.Areas.Admin.Models;

/// <summary>Bulgarian display names of the roles.</summary>
public static class RoleNames
{
    public static string Label(string role) => role switch
    {
        Roles.Administrator => "Администратор",
        Roles.Teacher => "Учител",
        Roles.Student => "Ученик",
        Roles.Parent => "Родител",
        _ => role
    };
}
