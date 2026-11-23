using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.ViewModels;

/// <summary>
/// The icon and the color of a subject, safe to put into a class attribute and a CSS variable. The values
/// are checked against the closed lists again: a value that got into the database some other way is replaced.
/// </summary>
public static class SubjectLook
{
    public static (string Icon, string Color) Of(string? icon, string? color) => (
        SubjectAppearance.IsValidIcon(icon) ? icon! : SubjectAppearance.DefaultIcon,
        SubjectAppearance.IsValidColor(color) ? color! : SubjectAppearance.DefaultColor);

    public static (string Icon, string Color) Of(Category subject) => Of(subject.Icon, subject.Color);
}
