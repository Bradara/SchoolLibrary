using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Extensions;

public static class ResourceExtensions
{
    /// <summary>
    /// Who is shown as the publisher. An imported free resource never had a person behind it,
    /// while a lesson without an owner belongs to an account that was deleted.
    /// </summary>
    public static string PublisherName(this Resource resource) =>
        resource.Owner?.FullName
        ?? (resource.Kind == ResourceKind.External ? "Платформата" : "Изтрит потребител");

    /// <summary>Name of a single resource of this kind (for a badge or a heading).</summary>
    public static string Label(this ResourceKind kind) => kind switch
    {
        ResourceKind.External => "Свободен ресурс",
        ResourceKind.Upload => "Качен материал",
        _ => "Урок"
    };

    /// <summary>Name of the section that lists resources of this kind (for the menu and the breadcrumb).</summary>
    public static string SectionTitle(this ResourceKind kind) => kind switch
    {
        ResourceKind.External => "Свободни ресурси",
        ResourceKind.Upload => "Качени материали",
        _ => "Уроци"
    };
}
