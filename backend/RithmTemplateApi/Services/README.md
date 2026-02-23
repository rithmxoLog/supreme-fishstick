# Services

API-level services that support controller operations but are specific to the HTTP/presentation layer.

## Purpose

This folder contains services that:

- **Bridge API and Business Logic**: Adapt business operations for HTTP consumption
- **Handle Cross-Cutting Concerns**: Notifications, progress tracking, caching strategies
- **Remain HTTP-Aware**: Unlike Helpers, these services can understand HTTP context

## When to Use Services vs Helpers

| Aspect | Services (here) | Helpers |
|--------|-----------------|---------|
| HTTP Context | Can access | Should not access |
| Reusability | API-specific | Reusable across projects |
| Dependencies | Can depend on ASP.NET Core | Pure .NET libraries only |
| Examples | Progress notifiers, Response formatters | Business logic, External integrations |

## Current Services

### IProgressNotifier / SignalRProgressNotifier

Notifies clients about operation progress in real-time.

**Use Case**: Long-running batch operations that return `202 Accepted`

```csharp
public interface IProgressNotifier
{
    Task NotifyProgressAsync(string operationId, int progress, string? message);
    Task NotifyCompletionAsync(string operationId, bool success, string? message, object? result);
    Task NotifyErrorAsync(string operationId, string errorMessage, string? errorCode);
}
```

**Usage in Controller**:

```csharp
[HttpPost("bulk-import")]
public async Task<IActionResult> BulkImport([FromBody] BulkImportRequest request)
{
    var operation = await _operationManager.StartOperationAsync(
        "BulkImport",
        request,
        async (input, progress, ct) =>
        {
            // Processing logic here...
            await _notifier.NotifyProgressAsync(operationId, 50, "Halfway done");
            return result;
        },
        UserRithmId!,
        OrgRithmId!);

    return AcceptedResponse(operation.OperationId, operation.StatusUrl);
}
```

## Adding New Services

1. Define an interface (`IMyService`)
2. Implement the service (`MyService`)
3. Register in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IMyService, MyService>();
   ```

## Lifecycle Guidelines

- **Singleton**: For stateless services with expensive initialization (SignalR connections)
- **Scoped**: For services that need per-request state
- **Transient**: For lightweight, stateless services
