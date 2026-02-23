using RithmTemplateApi.XoPublic.Models;

namespace RithmTemplateApi.XoPublic.Services;

/// <summary>
/// Discovers and validates the xo_public schema at runtime.
/// Queries PostgreSQL system catalogs (information_schema, pg_catalog) to inspect
/// the integration contract: views, columns, SQL comments, user permissions.
/// </summary>
public interface IXoPublicSchemaDiscoveryService
{
    /// <summary>
    /// Checks whether the xo_public schema exists in the database.
    /// </summary>
    Task<bool> SchemaExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for all views in xo_public, including column details
    /// and SQL comments (from pg_description).
    /// </summary>
    Task<List<XoPublicViewMetadata>> GetViewsMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the dedicated security user (rithm_integrator_svc) exists.
    /// </summary>
    Task<bool> SecurityUserExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the security user has correct permissions:
    /// USAGE on xo_public, SELECT on all views, NO access to public schema.
    /// </summary>
    Task<bool> SecurityUserHasCorrectPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that ALL views in xo_public have a tenant_id column.
    /// Required for multi-tenant isolation in IntegratorXO queries.
    /// </summary>
    Task<bool> AllViewsHaveTenantIdAsync(CancellationToken cancellationToken = default);
}
