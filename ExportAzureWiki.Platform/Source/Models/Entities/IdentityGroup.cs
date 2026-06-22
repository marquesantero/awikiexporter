namespace ExportAzureWiki.Models.Entities;

/// <summary>
/// Identity Group entity for database storage
/// </summary>
public class IdentityGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; } = false;
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
