using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Licenses;

/// <summary>What an administrator types in the license form.</summary>
public record LicenseInput(string Name, string? Description, string? Url);

/// <summary>A license with the number of resources shared under it, for the admin list.</summary>
public record LicenseListItem(License License, int ResourceCount);
