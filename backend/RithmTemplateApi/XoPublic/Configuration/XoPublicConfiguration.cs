namespace RithmTemplateApi.XoPublic.Configuration;

/// <summary>
/// Configuration for the xo_public integration module.
/// Maps to the "XoPublic" section in appsettings.json.
/// </summary>
public class XoPublicConfiguration
{
    /// <summary>
    /// PostgreSQL schema name for the public integration contract.
    /// Default: "xo_public"
    /// </summary>
    public string SchemaName { get; set; } = "xo_public";

    /// <summary>
    /// Dedicated PostgreSQL user for IntegratorXO access (SELECT-only on xo_public).
    /// Default: "rithm_integrator_svc"
    /// </summary>
    public string SecurityUserName { get; set; } = "rithm_integrator_svc";

    /// <summary>
    /// Whether the GET /api/xo/metadata endpoint is enabled.
    /// </summary>
    public bool MetadataEndpointEnabled { get; set; } = true;
}
