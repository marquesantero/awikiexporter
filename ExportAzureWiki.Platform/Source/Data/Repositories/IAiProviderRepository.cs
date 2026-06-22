using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Repositories;

public interface IAiProviderRepository : IRepository<AiProvider>
{
    Task<AiProvider?> GetByProviderNameAsync(string providerName);
    Task<IEnumerable<AiProvider>> GetEnabledProvidersAsync();
    Task<AiProvider?> GetDefaultProviderAsync();
    Task<bool> SetEnabledAsync(int id, bool isEnabled);
    Task<bool> SetAsDefaultAsync(int id);
    Task<bool> UpdateSafeAsync(AiProvider provider);
}
