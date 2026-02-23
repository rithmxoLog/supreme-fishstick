using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RithmTemplate.DAL.Persistence;
using RithmTemplateApi.XoPublic.Configuration;
using RithmTemplateApi.XoPublic.Models;

namespace RithmTemplateApi.XoPublic.Services;

/// <summary>
/// Discovers and validates the xo_public schema by querying PostgreSQL system catalogs.
/// Uses raw ADO.NET (not EF Core) because it reads information_schema and pg_catalog.
/// </summary>
public class XoPublicSchemaDiscoveryService : IXoPublicSchemaDiscoveryService
{
    private readonly RithmTemplateDbContext _dbContext;
    private readonly XoPublicConfiguration _config;
    private readonly ILogger<XoPublicSchemaDiscoveryService> _logger;

    public XoPublicSchemaDiscoveryService(
        RithmTemplateDbContext dbContext,
        IOptions<XoPublicConfiguration> config,
        ILogger<XoPublicSchemaDiscoveryService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SchemaExistsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema";

        await using var connection = await GetOpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParameter(cmd, "@schema", _config.SchemaName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task<List<XoPublicViewMetadata>> GetViewsMetadataAsync(CancellationToken cancellationToken = default)
    {
        var views = new List<XoPublicViewMetadata>();

        // Step 1: Get all views with their descriptions
        var viewsSql = @"
            SELECT
                v.table_name AS view_name,
                obj_description(c.oid) AS view_description
            FROM information_schema.views v
            JOIN pg_catalog.pg_class c ON c.relname = v.table_name
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace AND n.nspname = v.table_schema
            WHERE v.table_schema = @schema
            ORDER BY v.table_name";

        await using var connection = await GetOpenConnectionAsync(cancellationToken);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = viewsSql;
            AddParameter(cmd, "@schema", _config.SchemaName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                views.Add(new XoPublicViewMetadata
                {
                    ViewName = reader.GetString(0),
                    Description = reader.IsDBNull(1) ? null : reader.GetString(1)
                });
            }
        }

        // Step 2: For each view, get columns with descriptions
        foreach (var view in views)
        {
            var columnsSql = @"
                SELECT
                    col.column_name,
                    col.data_type,
                    col.is_nullable,
                    col.ordinal_position,
                    col_description(cls.oid, col.ordinal_position::int) AS column_description
                FROM information_schema.columns col
                JOIN pg_catalog.pg_class cls ON cls.relname = col.table_name
                JOIN pg_catalog.pg_namespace ns ON ns.oid = cls.relnamespace AND ns.nspname = col.table_schema
                WHERE col.table_schema = @schema AND col.table_name = @viewName
                ORDER BY col.ordinal_position";

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = columnsSql;
            AddParameter(cmd, "@schema", _config.SchemaName);
            AddParameter(cmd, "@viewName", view.ViewName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                view.Columns.Add(new XoPublicColumnMetadata
                {
                    ColumnName = reader.GetString(0),
                    DataType = reader.GetString(1),
                    IsNullable = reader.GetString(2) == "YES",
                    OrdinalPosition = reader.GetInt32(3),
                    Description = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }

        _logger.LogDebug("Discovered {ViewCount} views in {Schema}", views.Count, _config.SchemaName);
        return views;
    }

    public async Task<bool> SecurityUserExistsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT 1 FROM pg_roles WHERE rolname = @rolname";

        await using var connection = await GetOpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParameter(cmd, "@rolname", _config.SecurityUserName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task<bool> SecurityUserHasCorrectPermissionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken);

        // Check 1: Has USAGE on xo_public schema
        var hasSchemaUsage = await CheckPrivilegeAsync(
            connection,
            $"SELECT has_schema_privilege('{_config.SecurityUserName}', '{_config.SchemaName}', 'USAGE')",
            cancellationToken);

        if (!hasSchemaUsage)
        {
            _logger.LogWarning("Security user {User} lacks USAGE privilege on schema {Schema}",
                _config.SecurityUserName, _config.SchemaName);
            return false;
        }

        // Check 2: Has SELECT on all views in xo_public
        var viewNamesSql = @"
            SELECT table_name FROM information_schema.views
            WHERE table_schema = @schema";

        var viewNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = viewNamesSql;
            AddParameter(cmd, "@schema", _config.SchemaName);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                viewNames.Add(reader.GetString(0));
            }
        }

        foreach (var viewName in viewNames)
        {
            var hasSelect = await CheckPrivilegeAsync(
                connection,
                $"SELECT has_table_privilege('{_config.SecurityUserName}', '{_config.SchemaName}.{viewName}', 'SELECT')",
                cancellationToken);

            if (!hasSelect)
            {
                _logger.LogWarning("Security user {User} lacks SELECT privilege on {Schema}.{View}",
                    _config.SecurityUserName, _config.SchemaName, viewName);
                return false;
            }
        }

        // Check 3: Does NOT have access to public schema
        var hasPublicAccess = await CheckPrivilegeAsync(
            connection,
            $"SELECT has_schema_privilege('{_config.SecurityUserName}', 'public', 'USAGE')",
            cancellationToken);

        if (hasPublicAccess)
        {
            _logger.LogWarning("Security user {User} has USAGE on public schema — should be revoked",
                _config.SecurityUserName);
            return false;
        }

        return true;
    }

    public async Task<bool> AllViewsHaveTenantIdAsync(CancellationToken cancellationToken = default)
    {
        // Find views that do NOT have a tenant_id column
        var sql = @"
            SELECT v.table_name
            FROM information_schema.views v
            WHERE v.table_schema = @schema
              AND v.table_name NOT IN (
                SELECT c.table_name
                FROM information_schema.columns c
                WHERE c.table_schema = @schema AND c.column_name = 'tenant_id'
              )";

        await using var connection = await GetOpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParameter(cmd, "@schema", _config.SchemaName);

        var viewsWithoutTenantId = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            viewsWithoutTenantId.Add(reader.GetString(0));
        }

        if (viewsWithoutTenantId.Count > 0)
        {
            _logger.LogWarning(
                "Views in {Schema} missing tenant_id column: {Views}",
                _config.SchemaName,
                string.Join(", ", viewsWithoutTenantId));
            return false;
        }

        return true;
    }

    private async Task<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        return connection;
    }

    private static async Task<bool> CheckPrivilegeAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool b && b;
    }

    private static void AddParameter(DbCommand cmd, string name, string value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
