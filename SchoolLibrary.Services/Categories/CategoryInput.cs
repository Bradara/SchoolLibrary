namespace SchoolLibrary.Services.Categories;

/// <summary>
/// The fields of a subject that can be set. Icon and Color are values from
/// <see cref="SchoolLibrary.Data.Common.SubjectAppearance"/>; empty means "not chosen".
/// </summary>
public record CategoryInput(string Name, string? Icon, string? Color);
