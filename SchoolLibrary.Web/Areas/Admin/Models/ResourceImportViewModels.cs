using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Areas.Admin.Models;

/// <summary>The names an administrator can use in the CSV file, shown next to the upload form.</summary>
public class ResourceImportIndexViewModel
{
    public IReadOnlyList<string> Licenses { get; set; } = new List<string>();
    public IReadOnlyList<string> Subjects { get; set; } = new List<string>();
    public IReadOnlyList<string> Grades { get; set; } = new List<string>();
}

/// <summary>An analysed file (preview) or the outcome of an import (result).</summary>
public class ResourceImportViewModel
{
    public ResourceImportPreview Preview { get; set; } = new();

    /// <summary>The file text, posted back with the confirm button so the server keeps no state.</summary>
    public string CsvText { get; set; } = string.Empty;

    public string? FileName { get; set; }
}
