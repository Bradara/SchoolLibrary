using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SchoolLibrary.ViewModels;

/// <summary>A new version of the file of an uploaded resource; the details of the resource stay as they are.</summary>
public class ResourceReplaceFileViewModel
{
    public int Id { get; set; }

    [Display(Name = "Нов файл")]
    public IFormFile? File { get; set; }

    /// <summary>The first page of a new PDF, drawn by the browser (as on the upload form).</summary>
    public IFormFile? Cover { get; set; }

    // The resource and its current file, filled by the controller
    [BindNever]
    public string Title { get; set; } = string.Empty;

    [BindNever]
    public string CurrentFileName { get; set; } = string.Empty;

    [BindNever]
    public long CurrentFileSizeBytes { get; set; }
}
