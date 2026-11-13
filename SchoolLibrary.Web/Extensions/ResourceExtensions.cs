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
}
