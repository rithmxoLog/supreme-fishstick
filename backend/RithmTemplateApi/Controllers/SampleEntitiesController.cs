using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rithm.Infrastructure.BatchProcessing;
using RithmTemplate.Application.BatchProcessing.Orchestrators;
using RithmTemplate.Application.Exceptions;
using RithmTemplate.Application.Features.SampleEntities.Commands.CreateSampleEntity;
using RithmTemplate.Application.Features.SampleEntities.Queries.GetSampleEntityById;

namespace RithmTemplateApi.Controllers;

/// <summary>
/// Controller for SampleEntity operations.
/// Demonstrates MediatR + CQRS pattern usage and batch operations with 202 Accepted.
/// </summary>
[Authorize]
public class SampleEntitiesController : RithmTemplateBaseController
{
    private readonly IMediator _mediator;
    private readonly IMassiveOperationManager _operationManager;
    private readonly SampleBulkImportOrchestrator _bulkImportOrchestrator;

    public SampleEntitiesController(
        IMediator mediator,
        IMassiveOperationManager operationManager,
        SampleBulkImportOrchestrator bulkImportOrchestrator)
    {
        _mediator = mediator;
        _operationManager = operationManager;
        _bulkImportOrchestrator = bulkImportOrchestrator;
    }

    /// <summary>
    /// Gets a SampleEntity by ID.
    /// </summary>
    /// <param name="id">The entity ID.</param>
    /// <returns>The SampleEntity if found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SampleEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(Guid id)
    {
        // No validation needed - middleware guarantees tenant context

        var query = new GetSampleEntityByIdQuery
        {
            Id = id,
            TenantId = TenantId  // From base controller
        };

        var result = await _mediator.Send(query);

        if (result is null)
            throw new NotFoundException("SampleEntity", id.ToString());

        return OkResponse(result);
    }

    /// <summary>
    /// Creates a new SampleEntity.
    /// </summary>
    /// <param name="request">The creation request.</param>
    /// <returns>The created entity info.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(CreateSampleEntityResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateSampleEntityRequest request)
    {
        // No validation needed - middleware guarantees tenant context

        var command = new CreateSampleEntityCommand
        {
            Name = request.Name,
            Description = request.Description,
            Priority = request.Priority,
            TenantId = TenantId,             // From base controller
            CreatedBy = ActorId ?? "system"   // From base controller
        };

        var result = await _mediator.Send(command);

        return CreatedResponse(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Bulk imports SampleEntities (async operation).
    /// Returns 202 Accepted with operation tracking information.
    /// Use the returned statusUrl to poll for progress.
    /// </summary>
    /// <param name="request">The bulk import request.</param>
    /// <returns>Operation info for tracking.</returns>
    [HttpPost("bulk-import")]
    [ProducesResponseType(typeof(OperationInfo), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BulkImport([FromBody] BulkImportApiRequest request)
    {
        // No validation needed - middleware guarantees tenant context

        if (request.Items == null || request.Items.Count == 0)
            throw new ValidationException("Items", "At least one item is required");

        var bulkRequest = new BulkImportRequest
        {
            TenantId = TenantId,              // From base controller
            UserId = ActorId ?? "system",      // From base controller
            Items = request.Items.Select(i => new BulkImportItem
            {
                Name = i.Name,
                Description = i.Description,
                Priority = i.Priority
            }).ToList()
        };

        var operation = await _operationManager.StartOperationAsync(
            "SampleEntity Bulk Import",
            bulkRequest,
            (input, progress, ct) => _bulkImportOrchestrator.ExecuteAsync(input, progress, ct),
            ActorId ?? "system",    // From base controller
            TenantId);              // From base controller

        return AcceptedResponse(operation.OperationId, $"/api/operations/{operation.OperationId}/status");
    }
}

/// <summary>
/// Request model for creating a SampleEntity.
/// </summary>
public class CreateSampleEntityRequest
{
    /// <summary>
    /// Name of the entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Priority (1-10). Default is 5.
    /// </summary>
    public int Priority { get; init; } = 5;
}

/// <summary>
/// Request model for bulk import API.
/// </summary>
public class BulkImportApiRequest
{
    /// <summary>
    /// Items to import.
    /// </summary>
    public required List<BulkImportItemRequest> Items { get; init; }
}

/// <summary>
/// Single item in bulk import API request.
/// </summary>
public class BulkImportItemRequest
{
    /// <summary>
    /// Name of the entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Priority (1-10). Default is 5.
    /// </summary>
    public int Priority { get; init; } = 5;
}
