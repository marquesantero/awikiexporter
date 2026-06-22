using ExportAzureWiki.Data;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Platform.Setup;

public interface ISetupService
{
    Task<bool> IsFirstRunAsync();
    Task<bool> ConfigureDatabaseAsync(DatabaseConfiguration config, Action<string>? progressCallback = null);
    Task<bool> CreateAdminUserAsync(string username, string email, string password);
    Task<bool> CompleteSetupAsync();
    Task<bool> TestConnectionAsync(DatabaseConfiguration config);
    Task<bool> AdminUserExistsAsync();
    IDbConnectionFactory GetConnectionFactory();
}
