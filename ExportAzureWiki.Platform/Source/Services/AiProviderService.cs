using ExportAzureWiki.Data;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Services;

public sealed class AiProviderService
{
    private readonly IUnitOfWork _unitOfWork;

    public AiProviderService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AiProvider?> GetDefaultActiveProviderAsync()
    {
        var preferred = await _unitOfWork.AiProviders.GetDefaultProviderAsync().ConfigureAwait(false);
        if (preferred?.IsEnabled == true)
        {
            return preferred;
        }

        var enabled = await _unitOfWork.AiProviders.GetEnabledProvidersAsync().ConfigureAwait(false);
        return enabled.FirstOrDefault();
    }
}
