namespace SchoolLibrary.Data.Constants;

/// <summary>
/// Names of the application roles. Every registered user is in <see cref="User"/> and in
/// one more role that describes who they are.
/// </summary>
public static class Roles
{
    /// <summary>Base role of every registered user.</summary>
    public const string User = "User";

    /// <summary>Manages subjects, grades, licenses, users and reports; can delete any resource.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Can add, edit and delete own resources.</summary>
    public const string Teacher = "Teacher";

    /// <summary>Can browse resources, keep favorites, comment and report.</summary>
    public const string Student = "Student";

    /// <summary>Same rights as a student.</summary>
    public const string Parent = "Parent";

    /// <summary>Name used before the roles were renamed. Kept only to migrate existing databases.</summary>
    public const string LegacyAdmin = "Admin";

    /// <summary>Roles that may add and edit resources (use in [Authorize(Roles = ...)]).</summary>
    public const string TeacherOrAdministrator = Teacher + "," + Administrator;

    /// <summary>Roles an administrator can give or take away (User is always kept).</summary>
    public static readonly string[] Assignable = { Administrator, Teacher, Student, Parent };

    public static readonly string[] All ={ User, Administrator, Teacher, Student, Parent };
}
