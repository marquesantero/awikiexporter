using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services;

public sealed class DocumentExportService : IDocumentExportService
{
    private readonly IExportBackend _backend;

    public DocumentExportService()
        : this(new ExportBackend())
    {
    }

    internal DocumentExportService(IExportBackend backend)
    {
        _backend = backend;
    }

    public Task ExportToWordAsync(string html, string filePath, bool applyWordFineTune = false, bool refreshImageCache = false)
    {
        return _backend.ExportToWordAsync(html, filePath, applyWordFineTune, refreshImageCache);
    }

    public Task ExportToPdfAsync(string html, string filePath)
    {
        return _backend.ExportToPdfAsync(html, filePath);
    }
}







