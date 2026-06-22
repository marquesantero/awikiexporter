using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Services;
using CoreModels = ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Platform.Services;

/// <summary>
/// Probes an AI provider (model discovery + connection test) for the provider
/// currently being edited. Maps the Core DTO to the internal model and reuses
/// the OpenAI-compatible endpoint/auth logic in <see cref="AiTextOperationsService"/>.
/// </summary>
public sealed class AiProviderProbeService : IAiProviderProbeService
{
    private readonly AiTextOperationsService _operations;

    public AiProviderProbeService(IDbConnectionFactory dbConnectionFactory)
    {
        var unitOfWork = new UnitOfWork(dbConnectionFactory);
        _operations = new AiTextOperationsService(new AiProviderService(unitOfWork));
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CoreModels.AiProvider provider, CancellationToken cancellationToken = default)
        => _operations.ListModelsAsync(ProviderModelMapper.ToProvider(provider), cancellationToken);

    public async Task<AiProviderProbeResult> TestAsync(CoreModels.AiProvider provider, CancellationToken cancellationToken = default)
    {
        var (success, message, models) = await _operations
            .TestConnectionAsync(ProviderModelMapper.ToProvider(provider), cancellationToken)
            .ConfigureAwait(false);
        return new AiProviderProbeResult(success, message, models);
    }
}
