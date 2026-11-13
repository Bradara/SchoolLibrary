namespace SchoolLibrary.Services.Resources;

/// <summary>What the import would do (or did) with one row of the CSV file.</summary>
public enum ResourceImportRowStatus
{
    /// <summary>The row is valid and its link is not in the library yet.</summary>
    New,

    /// <summary>The link is already in the library or appears earlier in the same file.</summary>
    Duplicate,

    /// <summary>The row breaks a rule (missing field, unknown subject, bad link, ...).</summary>
    Invalid,

    /// <summary>The row was saved by the import.</summary>
    Imported
}

/// <summary>The outcome for one data row of the file.</summary>
public class ResourceImportRow
{
    /// <summary>Line of the file the row starts on (the header is line 1), so the admin can find it.</summary>
    public int LineNumber { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public ResourceImportRowStatus Status { get; set; }

    /// <summary>Why the row is invalid or a duplicate (Bulgarian).</summary>
    public string? Message { get; set; }

    /// <summary>The library resource that already has the same link, when there is one.</summary>
    public int? DuplicateOfId { get; init; }

    /// <summary>The data to save; set only for rows that passed the checks.</summary>
    public ResourceInput? Input { get; init; }
}

/// <summary>The analysed file: either a file-level error, or a status for every row.</summary>
public class ResourceImportPreview
{
    /// <summary>Set when the file cannot be read at all (empty, wrong separator, missing columns, too large).</summary>
    public string? FileError { get; init; }

    public IReadOnlyList<ResourceImportRow> Rows { get; init; } = new List<ResourceImportRow>();

    public bool HasFileError => FileError != null;

    public int NewCount => Rows.Count(r => r.Status == ResourceImportRowStatus.New);

    public int ImportedCount => Rows.Count(r => r.Status == ResourceImportRowStatus.Imported);

    public int DuplicateCount => Rows.Count(r => r.Status == ResourceImportRowStatus.Duplicate);

    public int InvalidCount => Rows.Count(r => r.Status == ResourceImportRowStatus.Invalid);

    public static ResourceImportPreview Failed(string message) => new() { FileError = message };
}
