# Feature Models

Domain-specific DTOs organized by feature/vertical slice.

## Purpose

This folder contains request and response models organized by business feature rather than technical layer. This approach:

- **Improves Discoverability**: All models for a feature are co-located
- **Enables Feature Isolation**: Changes to one feature don't affect others
- **Simplifies Deletion**: Removing a feature removes its entire folder
- **Aligns with Domain**: Structure mirrors business capabilities, not technical concerns

## Structure

```
Features/
├── Orders/
│   ├── CreateOrderRequest.cs
│   ├── UpdateOrderRequest.cs
│   ├── OrderResponse.cs
│   └── OrderListItem.cs
│
├── Products/
│   ├── ProductRequest.cs
│   └── ProductResponse.cs
│
└── Users/
    ├── UserProfileRequest.cs
    └── UserProfileResponse.cs
```

## Naming Conventions

| Type | Pattern | Example |
|------|---------|---------|
| Create Request | `Create{Entity}Request` | `CreateOrderRequest` |
| Update Request | `Update{Entity}Request` | `UpdateOrderRequest` |
| Full Response | `{Entity}Response` | `OrderResponse` |
| List Item | `{Entity}ListItem` | `OrderListItem` |
| Query/Filter | `{Entity}Query` | `OrderQuery` |

## Example Implementation

```csharp
// Features/Orders/CreateOrderRequest.cs
public class CreateOrderRequest
{
    [Required]
    [StringLength(50)]
    public required string ProductId { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; } = 1;

    public string? Notes { get; init; }
}

// Features/Orders/OrderResponse.cs
public class OrderResponse
{
    public required string Id { get; init; }
    public required string ProductId { get; init; }
    public required string ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal Total { get; init; }
    public string Status { get; init; } = "Pending";
    public DateTime CreatedAt { get; init; }
}

// Features/Orders/OrderListItem.cs (lighter version for lists)
public class OrderListItem
{
    public required string Id { get; init; }
    public required string ProductName { get; init; }
    public decimal Total { get; init; }
    public string Status { get; init; } = "Pending";
}
```

## Best Practices

1. **Separate Read/Write Models**: Don't reuse request models as responses
2. **List Items are Lighter**: Only include fields needed for list views
3. **Validation on Requests**: Use Data Annotations or FluentValidation
4. **No Entity References**: DTOs should never reference DAL entities directly
5. **Explicit Mapping**: Use explicit mapping (manual or AutoMapper) between DTOs and entities
