using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rithm.Platform.ServiceDiscovery.Models;
using RithmTemplateApi.Controllers;
using RithmTemplateApi.XoPublic.Configuration;
using RithmTemplateApi.XoPublic.Models;
using RithmTemplateApi.XoPublic.Services;

namespace RithmTemplateApi.XoPublic.Controllers;

/// <summary>
/// Exposes xo_public schema metadata for IntegratorXO consumption (Pilar 4).
/// Reads view/column information from information_schema + pg_description.
/// Protected by JWT — requires standard ecosystem headers (X-Tenant-Id, etc.).
/// </summary>
[ApiController]
[Route("api/xo")]
[Authorize]
public class XoMetadataController : RithmTemplateBaseController
{
    private readonly IXoPublicSchemaDiscoveryService _discoveryService;
    private readonly XoPublicConfiguration _xoConfig;
    private readonly InfraSoTBootstrapConfig _infraSoTConfig;
    private readonly ILogger<XoMetadataController> _logger;

    public XoMetadataController(
        IXoPublicSchemaDiscoveryService discoveryService,
        IOptions<XoPublicConfiguration> xoConfig,
        IOptions<InfraSoTBootstrapConfig> infraSoTConfig,
        ILogger<XoMetadataController> logger)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _xoConfig = xoConfig?.Value ?? throw new ArgumentNullException(nameof(xoConfig));
        _infraSoTConfig = infraSoTConfig?.Value ?? throw new ArgumentNullException(nameof(infraSoTConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns metadata for all views in the xo_public schema.
    /// Includes view names, column details, and SQL Comment descriptions.
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType(typeof(XoPublicMetadataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetMetadata(CancellationToken cancellationToken)
    {
        var schemaExists = await _discoveryService.SchemaExistsAsync(cancellationToken);

        if (!schemaExists)
        {
            _logger.LogWarning("xo_public schema not found — metadata endpoint returning 503");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                error = "xo_public schema not found. Run the XoPublicSchema migration first."
            });
        }

        var views = await _discoveryService.GetViewsMetadataAsync(cancellationToken);

        var response = new XoPublicMetadataResponse
        {
            SchemaName = _xoConfig.SchemaName,
            ServiceAppId = _infraSoTConfig.AppId,
            GeneratedAt = DateTime.UtcNow,
            Views = views
        };

        _logger.LogDebug("Returning xo_public metadata: {ViewCount} views", views.Count);

        return OkResponse(response);
    }
}
