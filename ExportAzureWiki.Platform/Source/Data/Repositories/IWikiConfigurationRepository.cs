using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository interface for WikiConfiguration entities
/// </summary>
public interface IWikiConfigurationRepository : IRepository<WikiConfiguration>
{
    /// <summary>
    /// Gets the default wiki configuration
    /// </summary>
    /// <returns>Default wiki configuration if found, null otherwise</returns>
    Task<WikiConfiguration?> GetDefaultWikiAsync();

    /// <summary>
    /// Gets a wiki configuration by its identifier
    /// </summary>
    /// <param name="organization">Organization name</param>
    /// <param name="project">Project name</param>
    /// <param name="wikiIdentifier">Wiki identifier</param>
    /// <returns>Wiki configuration if found, null otherwise</returns>
    Task<WikiConfiguration?> GetByIdentifierAsync(string organization, string project, string wikiIdentifier);

    /// <summary>
    /// Sets a wiki as the default
    /// </summary>
    /// <param name="wikiId">Wiki ID to set as default</param>
    /// <returns>True if successful</returns>
    Task<bool> SetDefaultWikiAsync(int wikiId);
}
