using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RithmTemplate.Application.Exceptions;
using Rithm.Platform.Security.Authorization.Exceptions;
using Rithm.Platform.Security.Audit.Exceptions;
using System.Net;
using System.Text.RegularExpressions;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Global exception handler using .NET 8's IExceptionHandler pattern.
/// Provides consistent error responses across the API.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = MapException(exception);

        // Log the exception
        LogException(exception, statusCode);

        // Set response details
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        // Add correlation ID if available
        if (httpContext.TraceIdentifier != null)
        {
            problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        }

        // Include stack trace in development
        if (_environment.IsDevelopment() && exception.StackTrace != null)
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true; // Exception was handled
    }

    private (int StatusCode, ProblemDetails ProblemDetails) MapException(Exception exception)
    {
        return exception switch
        {
            // Domain Exceptions
            NotFoundException notFound => (
                (int)HttpStatusCode.NotFound,
                new ProblemDetails
                {
                    Title = "Resource Not Found",
                    Detail = notFound.Message,
                    Status = (int)HttpStatusCode.NotFound,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4"
                }),

            // ValidationException must come before DomainException (inheritance)
            ValidationException validation => (
                (int)HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = validation.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Extensions = { ["errors"] = validation.ValidationErrors }
                }),

            // Tenant Violations (must come before DomainException due to inheritance)
            TenantMismatchException tenantMismatch => (
                (int)HttpStatusCode.Forbidden,
                new ProblemDetails
                {
                    Title = "Tenant Boundary Violation",
                    Detail = tenantMismatch.Message,
                    Status = (int)HttpStatusCode.Forbidden,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    Extensions =
                    {
                        ["errorCode"] = tenantMismatch.ErrorCode,
                        ["requestingTenant"] = tenantMismatch.RequestingTenantId,
                        ["dataOwner"] = tenantMismatch.DataOwnerTenantId
                    }
                }),

            TenantContextMissingException contextMissing => (
                (int)HttpStatusCode.Forbidden,
                new ProblemDetails
                {
                    Title = "Missing Tenant Context",
                    Detail = contextMissing.Message,
                    Status = (int)HttpStatusCode.Forbidden,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    Extensions = { ["errorCode"] = contextMissing.ErrorCode }
                }),

            DomainException domain => (
                (int)HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Domain Error",
                    Detail = domain.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                }),

            // Validation Exceptions
            ArgumentNullException argNull => (
                (int)HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Invalid Argument",
                    Detail = $"Required parameter '{argNull.ParamName}' was not provided",
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                }),

            ArgumentException arg => (
                (int)HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Invalid Argument",
                    Detail = arg.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                }),

            // Policy Engine & Audit Authority Exceptions
            PolicyEngineException policyEngine => (
                (int)HttpStatusCode.ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Policy Engine Unavailable",
                    Detail = "Authorization service is temporarily unavailable. Access denied for security.",
                    Status = (int)HttpStatusCode.ServiceUnavailable,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4"
                }),

            AuditAuthorityException auditAuthority => (
                (int)HttpStatusCode.ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Audit Service Unavailable",
                    Detail = "Audit service is temporarily unavailable. Operation cannot be completed without audit trail.",
                    Status = (int)HttpStatusCode.ServiceUnavailable,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4"
                }),

            // Unauthorized (includes PolicyDecisionId if present)
            UnauthorizedAccessException unauthorized => (
                (int)HttpStatusCode.Forbidden,
                new ProblemDetails
                {
                    Title = "Forbidden",
                    Detail = unauthorized.Message,
                    Status = (int)HttpStatusCode.Forbidden,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    Extensions = { ["policyDecisionId"] = ExtractPolicyDecisionId(unauthorized.Message) }
                }),

            // Concurrency
            InvalidOperationException invalidOp when invalidOp.Message.Contains("concurrent") => (
                (int)HttpStatusCode.Conflict,
                new ProblemDetails
                {
                    Title = "Conflict",
                    Detail = invalidOp.Message,
                    Status = (int)HttpStatusCode.Conflict,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
                }),

            InvalidOperationException invalidOp => (
                (int)HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Invalid Operation",
                    Detail = invalidOp.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                }),

            // Task/Operation Cancelled
            OperationCanceledException => (
                499, // Client Closed Request
                new ProblemDetails
                {
                    Title = "Request Cancelled",
                    Detail = "The request was cancelled by the client",
                    Status = 499,
                    Type = "https://httpstatuses.com/499"
                }),

            // Timeout
            TimeoutException => (
                (int)HttpStatusCode.GatewayTimeout,
                new ProblemDetails
                {
                    Title = "Gateway Timeout",
                    Detail = "The operation timed out",
                    Status = (int)HttpStatusCode.GatewayTimeout,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.5"
                }),

            // Default: Internal Server Error
            _ => (
                (int)HttpStatusCode.InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = _environment.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred. Please try again later.",
                    Status = (int)HttpStatusCode.InternalServerError,
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
                })
        };
    }

    private void LogException(Exception exception, int statusCode)
    {
        var logLevel = statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(
            logLevel,
            exception,
            "Unhandled exception occurred. Status: {StatusCode}, Message: {Message}",
            statusCode,
            exception.Message);
    }

    /// <summary>
    /// Extracts PolicyDecisionId from UnauthorizedAccessException message.
    /// Expected format: "...PolicyDecisionId: {guid}"
    /// </summary>
    private static string? ExtractPolicyDecisionId(string message)
    {
        // Pattern: PolicyDecisionId: {guid}
        var match = Regex.Match(message, @"PolicyDecisionId:\s*([a-fA-F0-9-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }
}
