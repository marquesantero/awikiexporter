using ExportAzureWiki.Data.Repositories;

namespace ExportAzureWiki.Data;

/// <summary>
/// Unit of Work pattern - manages transactions across multiple repositories
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// User repository
    /// </summary>
    IUserRepository Users { get; }

    /// <summary>
    /// Wiki configuration repository
    /// </summary>
    IWikiConfigurationRepository WikiConfigurations { get; }

    /// <summary>
    /// OAuth provider repository
    /// </summary>
    IOAuthProviderRepository OAuthProviders { get; }

    /// <summary>
    /// AI provider repository
    /// </summary>
    IAiProviderRepository AiProviders { get; }

    /// <summary>
    /// Identity group repository
    /// </summary>
    IGroupRepository Groups { get; }

    /// <summary>
    /// Authentication configuration repository
    /// </summary>
    AuthenticationConfigurationRepository AuthenticationConfiguration { get; }

    /// <summary>
    /// Begins a database transaction
    /// </summary>
    /// <returns>True if successful</returns>
    Task<bool> BeginTransactionAsync();

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <returns>True if successful</returns>
    Task<bool> CommitAsync();

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    /// <returns>True if successful</returns>
    Task<bool> RollbackAsync();
}
