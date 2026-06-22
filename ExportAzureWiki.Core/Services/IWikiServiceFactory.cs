using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Core.Services;

public interface IWikiServiceFactory
{
    IWikiService CreateService(WikiConfiguration configuration);
}
