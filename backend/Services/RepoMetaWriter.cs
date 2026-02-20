using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitXO.Api.Services;

/// <summary>
/// Writes a lightweight .meta.json file alongside each repository directory so that
/// ownership and visibility data can be recovered even if the PostgreSQL database is lost.
///
/// File location:  {reposBaseDir}/{repoName}.meta.json
/// Example:        repositories/my-project.meta.json
/// </summary>
public static class RepoMetaWriter
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    /// <summary>Write (or overwrite) the metadata file for a repository.</summary>
    public static async Task WriteAsync(
        string reposBaseDir,
        string repoName,
        string ownerUsername,
        string ownerEmail,
        long   ownerId,
        bool   isPublic,
        string? description,
        string  defaultBranch = "main",
        bool    ownerIsAdmin  = false)
    {
        var data = new RepoMetaFile
        {
            Name          = repoName,
            OwnerUsername = ownerUsername,
            OwnerEmail    = ownerEmail,
            OwnerId       = ownerId,
            IsPublic      = isPublic,
            Description   = description,
            DefaultBranch = defaultBranch,
            SavedAt       = DateTimeOffset.UtcNow,
            OwnerIsAdmin  = ownerIsAdmin,
        };

        var filePath = MetaFilePath(reposBaseDir, repoName);
        await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(data, _opts));
    }

    /// <summary>Delete the metadata file when a repository is deleted.</summary>
    public static void Delete(string reposBaseDir, string repoName)
    {
        var filePath = MetaFilePath(reposBaseDir, repoName);
        if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
    }

    /// <summary>
    /// Scan a directory for all *.meta.json files and return the parsed entries.
    /// Silently skips any files that cannot be parsed.
    /// </summary>
    public static IEnumerable<RepoMetaFile> ScanAll(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.GetFiles(dir, "*.meta.json"))
        {
            var meta = TryRead(file);
            if (meta != null) yield return meta;
        }
    }

    private static string MetaFilePath(string baseDir, string repoName) =>
        Path.Combine(baseDir, $"{repoName}.meta.json");

    private static RepoMetaFile? TryRead(string filePath)
    {
        try
        {
            var json = System.IO.File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<RepoMetaFile>(json);
        }
        catch { return null; }
    }
}

public class RepoMetaFile
{
    [JsonPropertyName("name")]          public string  Name          { get; set; } = "";
    [JsonPropertyName("ownerUsername")] public string  OwnerUsername { get; set; } = "";
    [JsonPropertyName("ownerEmail")]    public string  OwnerEmail    { get; set; } = "";
    [JsonPropertyName("ownerId")]       public long    OwnerId       { get; set; }
    [JsonPropertyName("isPublic")]      public bool    IsPublic      { get; set; }
    [JsonPropertyName("description")]   public string? Description   { get; set; }
    [JsonPropertyName("defaultBranch")] public string  DefaultBranch { get; set; } = "main";
    [JsonPropertyName("savedAt")]       public DateTimeOffset SavedAt { get; set; }
    [JsonPropertyName("ownerIsAdmin")]  public bool           OwnerIsAdmin  { get; set; }
}
