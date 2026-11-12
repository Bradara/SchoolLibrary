namespace SchoolLibrary.Services.Common;

/// <summary>One page of a larger result set.</summary>
public class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    /// <summary>Number of items on all pages together.</summary>
    public int TotalCount { get; }

    /// <summary>Current page, starting at 1.</summary>
    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}
