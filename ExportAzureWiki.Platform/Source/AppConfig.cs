namespace ExportAzureWiki;

public class AppConfig
{
    public string? OrganizationUrl { get; set; }
    public string? PersonalAccessToken { get; set; }
    public string? RepositoryId { get; set; }
    public string Projectname { get; set; } = string.Empty;
    public string WikiName { get; set; } = string.Empty;
}
