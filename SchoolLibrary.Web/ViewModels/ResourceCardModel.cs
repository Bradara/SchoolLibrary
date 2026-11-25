using System.Globalization;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.ViewModels;

/// <summary>
/// One resource on a catalog card (or a row of the list). <paramref name="IsFavorite"/> is null for a visitor
/// who is not signed in: no heart is shown then. <paramref name="ShowKind"/> is false in a section of one kind,
/// where the kind goes without saying.
/// </summary>
public record ResourceCardModel(Resource Resource, bool? IsFavorite, string ReturnUrl, bool ShowKind = true)
{
    private static readonly StringComparer Bulgarian =
        StringComparer.Create(CultureInfo.GetCultureInfo("bg-BG"), ignoreCase: true);

    /// <summary>The subjects in alphabetical order; the first one gives the card its color.</summary>
    public IReadOnlyList<Category> Subjects { get; } =
        Resource.Categories.OrderBy(c => c.Name, Bulgarian).ToList();

    public IReadOnlyList<GradeLevel> Grades { get; } =
        Resource.GradeLevels.OrderBy(g => g.Number).ToList();

    public MediaInfo Media { get; } = ResourceMedia.For(Resource);

    /// <summary>The color of the header band: the first subject's, or gray for a resource without a subject.</summary>
    public string Color => Subjects.Count > 0 ? SubjectLook.Of(Subjects[0]).Color : SubjectAppearance.DefaultColor;

    /// <summary>The large faint icon in the header: the first subject's, or the media's without a subject.</summary>
    public string HeaderIcon => Subjects.Count > 0 ? SubjectLook.Of(Subjects[0]).Icon : Media.Icon;

    /// <summary>
    /// The version of the book's cover for its address (the file id from the storage key), or null without a
    /// cover. A new cover gets a new address, so browsers may cache the picture for long.
    /// </summary>
    public string? CoverVersion => Resource.File?.CoverKey is { } key ? Path.GetFileNameWithoutExtension(key) : null;

    /// <summary>The author of the resource, or null when none is given.</summary>
    public string? Author => string.IsNullOrWhiteSpace(Resource.Author) ? null : Resource.Author.Trim();

    /// <summary>Whether only some roles may see the resource (an icon on the card says so).</summary>
    public bool IsRestricted => Resource.Audience != ResourceAudience.Everyone;

    public string VisibilityText => "Видимост: " + ResourceVisibility.Describe(Resource.Audience);
}
