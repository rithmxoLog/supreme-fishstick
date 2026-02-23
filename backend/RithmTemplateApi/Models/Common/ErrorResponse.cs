namespace RithmTemplateApi.Models.Common;

/// <summary>
/// Standardized error response model for API errors.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Indicates the operation failed.
    /// </summary>
    public bool Success { get; init; } = false;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Machine-readable error code for client-side handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Detailed validation errors, if applicable.
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Timestamp of when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a simple error response.
    /// </summary>
    public static ErrorResponse Create(string message, string? errorCode = null)
    {
        return new ErrorResponse
        {
            Message = message,
            ErrorCode = errorCode
        };
    }

    /// <summary>
    /// Creates a validation error response.
    /// </summary>
    public static ErrorResponse CreateValidationError(
        Dictionary<string, string[]> validationErrors,
        string? traceId = null)
    {
        return new ErrorResponse
        {
            Message = "One or more validation errors occurred.",
            ErrorCode = "VALIDATION_ERROR",
            ValidationErrors = validationErrors,
            TraceId = traceId
        };
    }
}
