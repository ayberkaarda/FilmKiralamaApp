namespace FilmKiralama.Domain.Entities;

/// <summary>One page of a list and the number of rows of the whole list.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>Number of pages, at least one (an empty list has one empty page).</summary>
    public int PageCount => Paging.PageCount(TotalCount, PageSize);
}

/// <summary>Page arithmetic of the paged lists.</summary>
public static class Paging
{
    /// <summary>Number of pages of <paramref name="totalCount"/> rows, at least one.</summary>
    public static int PageCount(int totalCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
    }

    /// <summary>
    /// The page that exists for <paramref name="requested"/>: 1 and above, at most the last page of
    /// <paramref name="totalCount"/> rows.
    /// </summary>
    public static int ClampPage(int requested, int totalCount, int pageSize) =>
        Math.Clamp(requested, 1, PageCount(totalCount, pageSize));
}
