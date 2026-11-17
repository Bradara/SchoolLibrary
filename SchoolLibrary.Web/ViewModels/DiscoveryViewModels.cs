using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.ViewModels;

/// <summary>One suggestion in the results table. Its fields are posted back so the ticked ones can be saved.</summary>
public class CandidateRow
{
    public bool IsSelected { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string? SourceName { get; set; }

    public string? Description { get; set; }

    public string LicenseName { get; set; } = string.Empty;

    // Only for display, not used when saving
    public bool IsEmbeddable { get; set; }

    public int? ExistingResourceId { get; set; }

    public static CandidateRow From(ResourceCandidate c) => new()
    {
        Title = c.Title,
        Url = c.Url,
        Author = c.Author,
        SourceName = c.SourceName,
        Description = c.Description,
        LicenseName = c.LicenseName,
        IsEmbeddable = c.IsEmbeddable,
        ExistingResourceId = c.ExistingResourceId
    };

    public CandidateSelection ToSelection() => new(Title, Url, Author, SourceName, Description, LicenseName);
}

/// <summary>The "Find resources" page: the search form, and the suggestions with the subject and grade choices.</summary>
public class DiscoveryIndexViewModel
{
    public string? Topic { get; set; }

    /// <summary>Every source that can be searched.</summary>
    public List<DiscoverySourceInfo> Sources { get; set; } = new();

    /// <summary>Keys of the sources that are ticked.</summary>
    public List<string> SelectedSources { get; set; } = new();

    /// <summary>True after a search ran, even when it found nothing.</summary>
    public bool Searched { get; set; }

    public List<CandidateRow> Candidates { get; set; } = new();

    /// <summary>The words that were actually searched for (joining words like "и" are left out).</summary>
    public List<string> SearchedTerms { get; set; } = new();

    /// <summary>Names of the sources that did not answer.</summary>
    public List<string> FailedSources { get; set; } = new();

    public List<Category> AllCategories { get; set; } = new();

    public List<GradeLevel> AllGradeLevels { get; set; } = new();
}

/// <summary>What is posted when the ticked suggestions are added.</summary>
public class DiscoveryAddViewModel
{
    /// <summary>The search to come back to after saving.</summary>
    public string? Topic { get; set; }

    public List<string> Sources { get; set; } = new();

    public List<CandidateRow> Candidates { get; set; } = new();

    public List<int> SelectedCategoryIds { get; set; } = new();

    public List<int> SelectedGradeLevelIds { get; set; } = new();
}
