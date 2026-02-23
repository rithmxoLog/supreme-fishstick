using System.Text.Json.Serialization;

namespace RithmTemplateApi.XoPublic.Models;

/// <summary>
/// Response DTO for GET /api/xo/metadata.
/// Describes the xo_public schema contract for IntegratorXO consumption.
/// </summary>
public class XoPublicMetadataResponse
{
    [JsonPropertyName("schema")]
    public string SchemaName { get; set; } = string.Empty;

    [JsonPropertyName("service_app_id")]
    public string ServiceAppId { get; set; } = string.Empty;

    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("views")]
    public List<XoPublicViewMetadata> Views { get; set; } = new();
}

/// <summary>
/// Metadata for a single view in xo_public.
/// Populated from information_schema + pg_description (SQL Comments).
/// </summary>
public class XoPublicViewMetadata
{
    [JsonPropertyName("view_name")]
    public string ViewName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("columns")]
    public List<XoPublicColumnMetadata> Columns { get; set; } = new();
}

/// <summary>
/// Metadata for a single column within an xo_public view.
/// </summary>
public class XoPublicColumnMetadata
{
    [JsonPropertyName("name")]
    public string ColumnName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string DataType { get; set; } = string.Empty;

    [JsonPropertyName("is_nullable")]
    public bool IsNullable { get; set; }

    [JsonPropertyName("ordinal_position")]
    public int OrdinalPosition { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
