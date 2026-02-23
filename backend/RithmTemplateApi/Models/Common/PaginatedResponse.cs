namespace RithmTemplateApi.Models.Common;

/// <summary>
/// Generic paginated response wrapper for list endpoints.
/// </summary>
/// <typeparam name="T">The type of items in the response.</typeparam>
public class PaginatedResponse<T>
{
    /// <summary>
    /// Indicates the operation succeeded.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// The items for the current page.
    /// </summary>
    public required IEnumerable<T> Data { get; init; }

    /// <summary>
    /// Pagination metadata.
    /// </summary>
    public required PaginationInfo Pagination { get; init; }

    /// <summary>
    /// Creates a paginated response from a collection.
    /// </summary>
    public static PaginatedResponse<T> Create(
        IEnumerable<T> items,
        int page,
        int pageSize,
        int totalCount)
    {
        return new PaginatedResponse<T>
        {
            Data = items,
            Pagination = new PaginationInfo
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                HasPreviousPage = page > 1,
                HasNextPage = page < (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        };
    }
}

/// <summary>
/// Pagination metadata.
/// </summary>
public class PaginationInfo
{
    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage { get; init; }

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage { get; init; }
}

/// <summary>
/// Request parameters for paginated queries.
/// </summary>
public class PaginationRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>
    /// Page number (1-based). Defaults to 1.
    /// </summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Number of items per page. Defaults to 20, max 100.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 20 : (value > 100 ? 100 : value);
    }

    /// <summary>
    /// Optional sorting field.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction (asc/desc). Defaults to asc.
    /// </summary>
    public string SortDirection { get; set; } = "asc";

    /// <summary>
    /// Number of items to skip for database queries.
    /// </summary>
    public int Skip => (Page - 1) * PageSize;
}
