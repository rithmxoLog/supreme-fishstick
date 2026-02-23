# Common Models

Shared DTOs (Data Transfer Objects) used across multiple features and endpoints.

## Purpose

This folder contains reusable models that:

- **Standardize API Responses**: Consistent structure for success, error, and paginated responses
- **Reduce Duplication**: Single source of truth for common patterns
- **Enforce Contracts**: Clients can rely on predictable response shapes

## Models

### ErrorResponse

Standardized error response for all API failures.

```json
{
  "success": false,
  "message": "Validation failed",
  "errorCode": "VALIDATION_ERROR",
  "validationErrors": {
    "email": ["Email is required", "Invalid email format"]
  },
  "traceId": "00-abc123...",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

### PaginatedResponse<T>

Wrapper for paginated list endpoints.

```json
{
  "success": true,
  "data": [...],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

### PaginationRequest

Query parameters for paginated endpoints.

```csharp
public class PaginationRequest
{
    public int Page { get; set; } = 1;        // 1-based
    public int PageSize { get; set; } = 20;   // Max 100
    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "asc";
}
```

## Usage in Controllers

```csharp
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] PaginationRequest pagination)
{
    var (items, totalCount) = await _service.GetPagedAsync(
        pagination.Page,
        pagination.PageSize);

    return Ok(PaginatedResponse<ItemDto>.Create(
        items,
        pagination.Page,
        pagination.PageSize,
        totalCount));
}

[HttpGet("{id}")]
public async Task<IActionResult> GetById(string id)
{
    var item = await _service.GetByIdAsync(id);
    if (item == null)
        return NotFound(ErrorResponse.Create($"Item '{id}' not found"));

    return OkResponse(item);
}
```

## Design Principles

1. **Immutability**: Use `init` accessors for response properties
2. **Required Properties**: Mark essential fields with `required` keyword
3. **Factory Methods**: Provide static `Create()` methods for complex construction
4. **No Business Logic**: Models should be pure data containers
