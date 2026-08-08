namespace ExportAzureWiki.Platform.Backend;

internal interface IExportBackend
{
    Task ExportToWordAsync(string html, string filePath, bool applyWordFineTune = false, bool refreshImageCache = false);
}







